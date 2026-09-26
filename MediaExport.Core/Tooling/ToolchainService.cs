// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace MediaExport.Core.Tooling;

public sealed record Tool(string Name, string Description, string PackageId, IReadOnlyList<string> Commands, bool IsRequired);

public sealed record ToolStatus(Tool Tool, string? Path, string? Version, bool CanUpdate, bool IsWingetAvailable)
{
    public bool IsInstalled => Path is not null;
    public bool CanInstall => !IsInstalled && IsWingetAvailable;

    public string StatusText => (IsInstalled, CanUpdate) switch
    {
        (false, _) => IsWingetAvailable ? "Not installed" : "Not installed. Install it and add it to PATH.",
        (true, true) => $"Version {Version} · update available",
        _ => Version is null ? "Installed" : $"Version {Version}"
    };
}

public sealed record ProcessResult(int ExitCode, string Output, string Error);

/// <summary>Finds yt-dlp, FFmpeg and Deno on PATH, and installs or updates them with WinGet.</summary>
public sealed partial class ToolchainService
{
    private readonly Func<string, IReadOnlyList<string>, CancellationToken, Task<ProcessResult>> _run;
    private readonly Func<string, string?> _resolve;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ToolchainService()
        : this(RunProcessAsync, ResolveOnPath)
    {
    }

    internal ToolchainService(
        Func<string, IReadOnlyList<string>, CancellationToken, Task<ProcessResult>> run,
        Func<string, string?> resolve)
    {
        _run = run;
        _resolve = resolve;
    }

    public static IReadOnlyList<Tool> Tools { get; } =
    [
        new("yt-dlp", "Searches for and downloads media", "yt-dlp.yt-dlp", ["yt-dlp"], true),
        new("FFmpeg", "Merges video with audio and converts formats", "Gyan.FFmpeg", ["ffmpeg", "ffprobe"], true),
        new("Deno", "Lets yt-dlp solve YouTube's playback challenges", "DenoLand.Deno", ["deno"], false)
    ];

    /// <summary>Required tools that can't be found, from a quick PATH scan.</summary>
    public IReadOnlyList<Tool> FindMissingRequiredTools()
    {
        RefreshProcessPath();
        return Tools.Where(tool => tool.IsRequired && !tool.Commands.All(command => _resolve(command) is not null)).ToArray();
    }

    public async Task<IReadOnlyList<ToolStatus>> CheckAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            RefreshProcessPath();
            // One listing is enough: a tool WinGet can upgrade is, by definition, one it manages. (WinGet is slow.)
            string? winget = _resolve("winget");
            string upgradable = winget is null
                ? string.Empty
                : (await _run(winget, ["list", "--upgrade-available", .. WingetFlags], cancellationToken)).Output;

            var statuses = new List<ToolStatus>();
            foreach (Tool tool in Tools)
            {
                string? path = tool.Commands.All(command => _resolve(command) is not null) ? _resolve(tool.Commands[0]) : null;
                string? version = path is null ? null : await GetVersionAsync(path, tool, cancellationToken);
                statuses.Add(new ToolStatus(
                    tool,
                    path,
                    version,
                    upgradable.Contains(tool.PackageId, StringComparison.OrdinalIgnoreCase),
                    winget is not null));
            }

            return statuses;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task InstallAsync(Tool tool, CancellationToken cancellationToken = default) =>
        RunWingetAsync("install", tool, cancellationToken);

    public Task UpdateAsync(Tool tool, CancellationToken cancellationToken = default) =>
        RunWingetAsync("upgrade", tool, cancellationToken);

    private static IReadOnlyList<string> WingetFlags { get; } =
        ["--source", "winget", "--accept-source-agreements", "--disable-interactivity"];

    private async Task RunWingetAsync(string verb, Tool tool, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            string winget = _resolve("winget") ?? throw new InvalidOperationException("WinGet isn't available, so tools can't be installed from here.");
            ProcessResult result = await _run(
                winget,
                [verb, "--id", tool.PackageId, "--exact", "--accept-package-agreements", .. WingetFlags],
                cancellationToken);

            if (result.ExitCode != 0)
            {
                string detail = (result.Error.Trim() is { Length: > 0 } error ? error : result.Output.Trim())
                    .Split('\n').LastOrDefault(line => line.Trim().Length > 0)?.Trim() ?? $"exit code {result.ExitCode}";
                throw new InvalidOperationException($"Couldn't {verb} {tool.Name}: {detail}");
            }

            RefreshProcessPath();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<string?> GetVersionAsync(string path, Tool tool, CancellationToken cancellationToken)
    {
        ProcessResult result = await _run(path, [tool.Commands[0] == "ffmpeg" ? "-version" : "--version"], cancellationToken);
        return result.ExitCode == 0 ? ParseVersion(result.Output) : null;
    }

    /// <summary>Picks the version out of lines like <c>ffmpeg version 7.1-full_build …</c> or <c>deno 2.4.1 (stable …)</c>.</summary>
    internal static string? ParseVersion(string output) =>
        VersionPattern().Match(output) is { Success: true } match ? match.Value : null;

    [GeneratedRegex(@"\d+(\.\d+)+")]
    private static partial Regex VersionPattern();

    // WinGet adds tools to the machine or user PATH, which this process only sees after a refresh.
    private static void RefreshProcessPath()
    {
        string[] entries = new[] { EnvironmentVariableTarget.Process, EnvironmentVariableTarget.Machine, EnvironmentVariableTarget.User }
            .SelectMany(target => (Environment.GetEnvironmentVariable("PATH", target) ?? string.Empty).Split(System.IO.Path.PathSeparator))
            .Select(entry => entry.Trim().Trim('"'))
            .Where(entry => entry.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Environment.SetEnvironmentVariable("PATH", string.Join(System.IO.Path.PathSeparator, entries));
    }

    private static string? ResolveOnPath(string command)
    {
        string[] extensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT;.COM").Split(';');
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(System.IO.Path.PathSeparator))
        {
            foreach (string extension in extensions)
            {
                string candidate = System.IO.Path.Combine(Environment.ExpandEnvironmentVariables(directory.Trim('"')), command + extension);
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
