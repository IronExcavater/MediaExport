// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MediaExport.Core.Tooling;

/// <param name="Repository">The GitHub repository whose releases carry Windows builds.</param>
/// <param name="AssetPattern">Matches the release asset to download (an .exe, or a .zip holding the executables).</param>
public sealed record Tool(
    string Id,
    string Name,
    string Repository,
    string AssetPattern,
    IReadOnlyList<string> Executables,
    bool IsRequired,
    string? Website = null,
    string? SourceCode = null)
{
    /// <summary>Where the project's code lives; the release repository unless that's only a build of it.</summary>
    public string SourceUrl => SourceCode ?? $"https://github.com/{Repository}";
}

public sealed record ToolRelease(string Version, string Tag, DateTimeOffset Published, string AssetUrl, long AssetSize);

/// <param name="IsManaged">Installed by MediaExport, so it can be updated or removed here.</param>
public sealed record ToolStatus(Tool Tool, string? Path, string? Version, bool IsManaged)
{
    public bool IsInstalled => Path is not null;
}

public sealed record ProcessResult(int ExitCode, string Output, string Error);

/// <summary>
/// Finds yt-dlp, FFmpeg and Deno, and installs, updates or removes copies MediaExport manages itself,
/// straight from each project's newest GitHub release. A managed copy wins over one on PATH.
/// </summary>
public sealed partial class ToolchainService
{
    private const string VersionFile = "version.txt";

    private readonly string _folder;
    private readonly HttpClient _http;
    private readonly Func<string, IReadOnlyList<string>, CancellationToken, Task<ProcessResult>> _run;
    private readonly Func<string, string?> _resolveOnPath;
    private readonly Dictionary<string, ToolRelease> _latest = [];

    public ToolchainService(string folder)
        : this(folder, CreateHttpClient(), RunProcessAsync, ResolveOnPath)
    {
    }

    internal ToolchainService(
        string folder,
        HttpClient http,
        Func<string, IReadOnlyList<string>, CancellationToken, Task<ProcessResult>> run,
        Func<string, string?> resolveOnPath)
    {
        _folder = folder;
        _http = http;
        _run = run;
        _resolveOnPath = resolveOnPath;
    }

    private static bool IsArm64 => RuntimeInformation.OSArchitecture == Architecture.Arm64;

    public static Tool YtDlp { get; } = new(
        "yt-dlp", "yt-dlp", "yt-dlp/yt-dlp",
        IsArm64 ? @"^yt-dlp_arm64\.exe$" : @"^yt-dlp\.exe$", ["yt-dlp"], true);

    public static Tool FFmpeg { get; } = new(
        "ffmpeg", "FFmpeg", "GyanD/codexffmpeg",
        @"essentials_build\.zip$", ["ffmpeg", "ffprobe"], true, "https://ffmpeg.org", "https://github.com/FFmpeg/FFmpeg");

    public static Tool Deno { get; } = new(
        "deno", "Deno", "denoland/deno",
        IsArm64 ? @"^deno-aarch64-pc-windows-msvc\.zip$" : @"^deno-x86_64-pc-windows-msvc\.zip$", ["deno"], false, "https://deno.com");

    public static IReadOnlyList<Tool> Tools { get; } = [YtDlp, FFmpeg, Deno];

    /// <summary>Where an executable runs from: the managed copy if there is one, otherwise PATH.</summary>
    public string? Locate(string executable)
    {
        string managed = Path.Combine(_folder, OwnerOf(executable)?.Id ?? executable, executable + ".exe");
        return File.Exists(managed) ? managed : _resolveOnPath(executable);
    }

    /// <summary>Required tools that can't be found anywhere.</summary>
    public IReadOnlyList<Tool> FindMissingRequiredTools()
    {
        RefreshProcessPath();
        return Tools.Where(tool => tool.IsRequired && tool.Executables.Any(executable => Locate(executable) is null)).ToArray();
    }

    public async Task<IReadOnlyList<ToolStatus>> CheckAsync(CancellationToken cancellationToken = default)
    {
        RefreshProcessPath();
        var statuses = new List<ToolStatus>();
        foreach (Tool tool in Tools)
            statuses.Add(await CheckAsync(tool, cancellationToken));
        return statuses;
    }

    public async Task<ToolStatus> CheckAsync(Tool tool, CancellationToken cancellationToken = default)
    {
        string? path = tool.Executables.All(executable => Locate(executable) is not null) ? Locate(tool.Executables[0]) : null;
        if (path is null)
            return new ToolStatus(tool, null, null, false);

        bool isManaged = path.StartsWith(_folder, StringComparison.OrdinalIgnoreCase);
        string? version = isManaged ? ReadManagedVersion(tool) : null;
        version ??= await GetVersionAsync(path, tool, cancellationToken);
        return new ToolStatus(tool, path, version, isManaged);
    }

    /// <summary>The newest release with a Windows build, or <c>null</c> if there's none. Remembered for the session.</summary>
    public async Task<ToolRelease?> GetLatestAsync(Tool tool, CancellationToken cancellationToken = default)
    {
        if (_latest.TryGetValue(tool.Id, out ToolRelease? known))
            return known;

        // A page of recent ones: the very newest may be a draft, a pre-release, or not have a Windows build yet.
        GitHubRelease[] releases = await _http.GetFromJsonAsync(
            $"https://api.github.com/repos/{tool.Repository}/releases?per_page=30",
            ToolchainJson.Default.GitHubReleaseArray,
            cancellationToken) ?? [];

        var pattern = new Regex(tool.AssetPattern, RegexOptions.IgnoreCase);
        ToolRelease? latest = releases
            .Where(release => !release.Draft && !release.Prerelease)
            .Select(release => (release, asset: release.Assets.FirstOrDefault(asset => pattern.IsMatch(asset.Name))))
            .Where(pair => pair.asset is not null)
            .Select(pair => new ToolRelease(pair.release.Tag.TrimStart('v'), pair.release.Tag, pair.release.Published, pair.asset!.Url, pair.asset.Size))
            .FirstOrDefault();

        if (latest is not null)
            _latest[tool.Id] = latest;
        return latest;
    }

    /// <summary>Installs the newest release as the managed copy, replacing whichever version was there.</summary>
    public async Task InstallAsync(Tool tool, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        ToolRelease release = await GetLatestAsync(tool, cancellationToken)
            ?? throw new InvalidOperationException($"{tool.Name} has no Windows release to install.");

        string download = Path.Combine(Path.GetTempPath(), $"MediaExport-{tool.Id}-{Guid.NewGuid():N}{Path.GetExtension(release.AssetUrl)}");
        string staging = Path.Combine(_folder, $".{tool.Id}-staging");
        try
        {
            await DownloadAsync(release, download, progress, cancellationToken);

            if (Directory.Exists(staging))
                Directory.Delete(staging, true);
            Directory.CreateDirectory(staging);

            if (download.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                ExtractExecutables(download, tool, staging);
            else
                File.Copy(download, Path.Combine(staging, tool.Executables[0] + ".exe"));

            await File.WriteAllTextAsync(Path.Combine(staging, VersionFile), release.Version, cancellationToken);

            // Swap in the new copy only once it's complete, so a failed install leaves the old one working.
            string target = Path.Combine(_folder, tool.Id);
            if (Directory.Exists(target))
                Directory.Delete(target, true);
            Directory.Move(staging, target);
        }
        finally
        {
            File.Delete(download);
            if (Directory.Exists(staging))
                Directory.Delete(staging, true);
        }
    }

    /// <summary>Removes the managed copy; one on PATH, if any, is used again.</summary>
    public void Remove(Tool tool)
    {
        string target = Path.Combine(_folder, tool.Id);
        if (Directory.Exists(target))
            Directory.Delete(target, true);
    }

    private async Task DownloadAsync(ToolRelease release, string path, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _http.GetAsync(release.AssetUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        long total = response.Content.Headers.ContentLength ?? release.AssetSize;

        await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using FileStream target = File.Create(path);
        byte[] buffer = new byte[81920];
        long received = 0;
        int read, reported = -1;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            received += read;

            // Each report is a trip to the UI thread, so only whole-percent steps are sent.
            int percent = total > 0 ? (int)(received * 100 / total) : -1;
            if (percent > reported)
            {
                reported = percent;
                progress?.Report(percent);
            }
        }
    }

    // Archives nest the executables in versioned folders; only the executables are kept, side by side.
    private static void ExtractExecutables(string archive, Tool tool, string folder)
    {
        using ZipArchive zip = ZipFile.OpenRead(archive);
        foreach (string executable in tool.Executables)
        {
            ZipArchiveEntry entry = zip.Entries.FirstOrDefault(entry => entry.Name.Equals(executable + ".exe", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"The {tool.Name} download doesn't contain {executable}.exe.");
            entry.ExtractToFile(Path.Combine(folder, entry.Name));
        }
    }

    private string? ReadManagedVersion(Tool tool)
    {
        string path = Path.Combine(_folder, tool.Id, VersionFile);
        return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
    }

    private async Task<string?> GetVersionAsync(string path, Tool tool, CancellationToken cancellationToken)
    {
        ProcessResult result = await _run(path, [tool == FFmpeg ? "-version" : "--version"], cancellationToken);
        return result.ExitCode == 0 ? ParseVersion(result.Output) : null;
    }

    private static Tool? OwnerOf(string executable) =>
        Tools.FirstOrDefault(tool => tool.Executables.Contains(executable, StringComparer.OrdinalIgnoreCase));

    /// <summary>Picks the version out of lines like <c>ffmpeg version 7.1-full_build …</c> or <c>deno 2.4.1 (stable …)</c>.</summary>
    internal static string? ParseVersion(string output) =>
        VersionPattern().Match(output) is { Success: true } match ? match.Value : null;

    [GeneratedRegex(@"\d+(\.\d+)+")]
    private static partial Regex VersionPattern();

    private static HttpClient CreateHttpClient()
    {
        var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MediaExport"); // GitHub's API refuses requests without one.
        return http;
    }

    // Installers add tools to the machine or user PATH, which this process only sees after a refresh.
    private static void RefreshProcessPath()
    {
        string[] entries = new[] { EnvironmentVariableTarget.Process, EnvironmentVariableTarget.Machine, EnvironmentVariableTarget.User }
            .SelectMany(target => (Environment.GetEnvironmentVariable("PATH", target) ?? string.Empty).Split(Path.PathSeparator))
            .Select(entry => entry.Trim().Trim('"'))
            .Where(entry => entry.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Environment.SetEnvironmentVariable("PATH", string.Join(Path.PathSeparator, entries));
    }

    private static string? ResolveOnPath(string command)
    {
        string[] extensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT;.COM").Split(';');
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
        {
            foreach (string extension in extensions)
            {
                string candidate = Path.Combine(Environment.ExpandEnvironmentVariables(directory.Trim('"')), command + extension);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    private static async Task<ProcessResult> RunProcessAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new ProcessResult(-1, string.Empty, exception.Message);
        }

        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        return new ProcessResult(process.ExitCode, await output, await error);
    }
}

internal sealed record GitHubAsset(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("browser_download_url")] string Url,
    [property: JsonPropertyName("size")] long Size);

internal sealed record GitHubRelease(
    [property: JsonPropertyName("tag_name")] string Tag,
    [property: JsonPropertyName("draft")] bool Draft,
    [property: JsonPropertyName("prerelease")] bool Prerelease,
    [property: JsonPropertyName("published_at")] DateTimeOffset Published,
    [property: JsonPropertyName("assets")] GitHubAsset[] Assets);

[JsonSerializable(typeof(GitHubRelease[]))]
internal sealed partial class ToolchainJson : JsonSerializerContext;
