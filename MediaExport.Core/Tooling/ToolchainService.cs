// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Settings;
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
        var installation = Find(id);
        if (installation?.Origin != ToolOrigin.Managed || !IsManagedPath(installation.Path))
            throw new InvalidOperationException($"{id} is not installed by MediaExport.");

        File.Delete(installation.Path);

        var tool = GetTool(id)!;
        tool.InstallPath = null;
        tool.IsInstalled = false;
        tool.Version = null;
        tool.LastCheckedUtc = null;
        settings.Save();

        return Task.CompletedTask;
    }

    private async Task<ToolInstallation> WriteManagedAsync(
        string id,
        Uri downloadUri,
        CancellationToken cancellationToken)
    {
        var tool = GetTool(id) ?? new ToolSettings { Id = id, Executable = id };
        var installDirectory = Path.Combine(Folder, id);
        Directory.CreateDirectory(installDirectory);

        var executable = tool.Executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? tool.Executable
            : tool.Executable + ".exe";
        var target = Path.GetFullPath(Path.Combine(installDirectory, executable));
        var temporary = Path.Combine(installDirectory, $"{Guid.NewGuid():N}.tmp");

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

            File.Move(temporary, target, overwrite: true);

            tool.InstallPath = target;
            tool.IsInstalled = true;
            tool.Version = "latest";
            tool.LastCheckedUtc = DateTimeOffset.UtcNow;
            settings.Current.Tools[id] = tool;
            settings.Save();

            return new ToolInstallation(target, ToolOrigin.Managed);
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

        return null;
    }

    /// <summary>Whether a program is a copy the app installed, which is in its own folder.</summary>
    public bool IsManaged(string path) => IsManagedPath(path);

    private bool IsManagedPath(string path)
    {
        var relative = Path.GetRelativePath(Folder, Path.GetFullPath(path));
        return !Path.IsPathRooted(relative)
            && relative != ".."
            && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
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
