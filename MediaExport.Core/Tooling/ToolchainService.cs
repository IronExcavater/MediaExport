// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Settings;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace MediaExport.Core.Tooling;

public sealed partial class ToolchainService(
    SettingsStore settings,
    ToolchainOptions options,
    HttpClient httpClient,
    IProcessRunner processRunner)
{
    public string Folder { get; } = Path.GetFullPath(options.ManagedFolder);

    public ToolSettings? GetTool(string id) =>
        settings.Current.Tools.TryGetValue(id, out var tool) ? tool : null;

    public bool IsInstalled(string id) => Find(id) is not null;

    public Task<ToolInstallation> InstallAsync(
        string id,
        Uri downloadUri,
        CancellationToken cancellationToken = default) =>
        WriteManagedAsync(id, downloadUri, cancellationToken);

    public async Task<ToolInstallation> UpdateAsync(
        string id,
        Uri downloadUri,
        CancellationToken cancellationToken = default)
    {
        var installation = Find(id);
        if (installation?.Origin != ToolOrigin.Managed || !IsManagedPath(installation.Path))
            throw new InvalidOperationException($"{id} is not installed by MediaExport.");

        return await WriteManagedAsync(id, downloadUri, cancellationToken);
    }

    public Task RemoveAsync(string id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateToolId(id);
        var installation = Find(id);
        if (installation?.Origin != ToolOrigin.Managed || !IsManagedPath(installation.Path))
            throw new InvalidOperationException($"{id} is not installed by MediaExport.");

        var toolDirectory = Path.GetFullPath(Path.Combine(Folder, id));
        if (IsManagedPath(toolDirectory) && IsWithinDirectory(toolDirectory, installation.Path))
        {
            Directory.Delete(toolDirectory, recursive: true);
        }
        else
        {
            File.Delete(installation.Path);
        }

        var tool = GetTool(id)!;
        tool.InstallPath = null;
        tool.IsInstalled = false;
        tool.Version = null;
        tool.LastCheckedUtc = null;
        settings.Save();

        return Task.CompletedTask;
    }

    private static string? ResolveExecutable(string root, string preferredExecutable)
    {
        var executable = preferredExecutable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? preferredExecutable
            : preferredExecutable + ".exe";

        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .FirstOrDefault(path =>
                string.Equals(Path.GetFileName(path), executable, StringComparison.OrdinalIgnoreCase))
            ?? Directory.EnumerateFiles(root, "*.exe", SearchOption.AllDirectories)
                .FirstOrDefault();
    }

    private static void CleanupOldBundles(string toolDirectory, string? activeBundleDirectory)
    {
        var activeBundle = activeBundleDirectory is null
            ? null
            : Path.GetFullPath(activeBundleDirectory);

        foreach (var bundleDirectory in Directory.EnumerateDirectories(
            toolDirectory, "*.bundle", SearchOption.TopDirectoryOnly))
        {
            if (activeBundle is not null &&
                string.Equals(
                    Path.GetFullPath(bundleDirectory),
                    activeBundle,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                Directory.Delete(bundleDirectory, recursive: true);
            }
            catch (IOException exception)
            {
                System.Diagnostics.Trace.TraceWarning(
                    $"Couldn't remove old tool bundle '{bundleDirectory}': {exception.Message}");
            }
            catch (UnauthorizedAccessException exception)
            {
                System.Diagnostics.Trace.TraceWarning(
                    $"Couldn't remove old tool bundle '{bundleDirectory}': {exception.Message}");
            }
        }
    }

    private async Task<ToolInstallation> WriteManagedAsync(
        string id,
        Uri downloadUri,
        CancellationToken cancellationToken)
    {
        ValidateToolId(id);
        var tool = GetTool(id) ?? new ToolSettings { Id = id, Executable = id };
        var installDirectory = Path.Combine(Folder, id);
        Directory.CreateDirectory(installDirectory);

        var executable = tool.Executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? tool.Executable
            : tool.Executable + ".exe";
        var target = Path.GetFullPath(Path.Combine(installDirectory, executable));
        var temporary = Path.Combine(installDirectory, $"{Guid.NewGuid():N}.tmp");
        var extractedDirectory = Path.Combine(installDirectory, $"{Guid.NewGuid():N}.bundle");

        try
        {
            using var response = await httpClient.GetAsync(
                downloadUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var destination = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true))
            {
                await source.CopyToAsync(destination, cancellationToken);
            }

            var isZip = downloadUri.AbsolutePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                    || (response.Content.Headers.ContentType?.MediaType?.Contains("zip", StringComparison.OrdinalIgnoreCase) ?? false);

            string installPath;

            if(isZip)
            {
                Directory.CreateDirectory(extractedDirectory);
                ZipFile.ExtractToDirectory(temporary, extractedDirectory);

                installPath = ResolveExecutable(extractedDirectory, executable)
                    ?? throw new InvalidOperationException($"Couldn't find {executable} in the zip file.");
            }
            else
            {
                File.Move(temporary, target, overwrite: true);
                installPath = target;
            }      

            tool.InstallPath = installPath;
            tool.IsInstalled = true;
            tool.Version = "latest";
            tool.LastCheckedUtc = DateTimeOffset.UtcNow;
            settings.Current.Tools[id] = tool;
            settings.Save();

            CleanupOldBundles(installDirectory, isZip ? extractedDirectory : null);
            return new ToolInstallation(installPath, ToolOrigin.Managed);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    /// <summary>Where a tool runs from: the copy the app installed if there is one, or else the one on PATH.</summary>
    /// <returns>The program's full path, or <c>null</c> if the tool isn't installed.</returns>
    public ToolInstallation? Find(string id)
    {
        var tool = GetTool(id);
        var installPath = tool?.InstallPath;
        if (File.Exists(installPath) && IsManagedPath(installPath))
            return new ToolInstallation(Path.GetFullPath(installPath), ToolOrigin.Managed);

        var executable = tool?.Executable ?? id;
        var fileName = executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? executable
            : executable + ".exe";

        foreach (var folder in options.SearchPath)
        {
            var candidate = Path.GetFullPath(Path.Combine(folder, fileName));
            if (File.Exists(candidate))
                return new ToolInstallation(candidate, ToolOrigin.External);
        }

        var managedToolFolder = Path.Combine(Folder, id);
        if (Directory.Exists(managedToolFolder))
        {
            var managedCandidate = Directory
                .EnumerateFiles(managedToolFolder, fileName, SearchOption.TopDirectoryOnly)
                .FirstOrDefault();

            if (managedCandidate is not null)
            {
                return new ToolInstallation(Path.GetFullPath(managedCandidate), ToolOrigin.Managed);
            }
        }

        return null;
    }

    private bool IsManagedPath(string path)
    {
        var relative = Path.GetRelativePath(Folder, Path.GetFullPath(path));
        return !Path.IsPathRooted(relative)
            && relative != ".."
            && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static bool IsWithinDirectory(string directory, string path)
    {
        var relative = Path.GetRelativePath(
            Path.GetFullPath(directory),
            Path.GetFullPath(path));

        return !Path.IsPathRooted(relative)
            && relative != ".."
            && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static void ValidateToolId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) ||
            id is "." or ".." ||
            !string.Equals(Path.GetFileName(id), id, StringComparison.Ordinal) ||
            id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("Tool id must be a simple directory name.", nameof(id));
        }
    }

    /// <summary>Asks a program for its version by running it.</summary>
    /// <param name="argument">What makes it print its version, such as <c>--version</c>.</param>
    /// <returns>The version, or <c>null</c> if the program wouldn't run or didn't say.</returns>
    public async Task<string> ReadVersionAsync(
        ToolInstallation installation,
        string argument,
        CancellationToken cancellationToken = default)
    {
        ProcessResult result;
        try
        {
            result = await processRunner.RunAsync(installation.Path, argument, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ToolInspectionException($"Couldn't inspect {installation.Path}.", exception);
        }

        if (result.ExitCode != 0)
        {
            var lines = result.StandardError.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var reason = lines.LastOrDefault() ?? $"The process exited with code {result.ExitCode}.";
            throw new ToolInspectionException($"Couldn't inspect {installation.Path}. {reason}");
        }

        return ParseVersion(result.StandardOutput)
            ?? throw new ToolInspectionException($"Couldn't read a version from {installation.Path}.");
    }

    /// <summary>Picks the version out of a line such as <c>ffmpeg version 7.1-full_build</c> or <c>2026.09.01</c>.</summary>
    internal static string? ParseVersion(string output)
    {
        var match = VersionPattern().Match(output);
        return match.Success ? match.Value : null;
    }

    [GeneratedRegex(@"\d+(\.\d+)+")]
    private static partial Regex VersionPattern();
}
