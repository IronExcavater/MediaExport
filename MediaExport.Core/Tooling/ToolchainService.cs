// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Settings;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace MediaExport.Core.Tooling;

public sealed partial class ToolchainService(SettingsStore settings, ToolchainOptions options)
{
    public string Folder { get; } = Path.GetFullPath(options.ManagedFolder);

    public ToolSettings? GetTool(string id) =>
        settings.Current.Tools.TryGetValue(id, out var tool) ? tool : null;

    public bool IsInstalled(string id) => Find(id) is not null;

    public async Task EnsureInstalledAsync(string id, string downloadUrl, CancellationToken cancellationToken = default)
    {
        var tool = GetTool(id) ?? new ToolSettings { Id = id, Executable = id };
        var installDir = Path.Combine(Folder, id);
        Directory.CreateDirectory(installDir);

        var targetFile = Path.Combine(
            installDir,
            tool.Executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? tool.Executable
                : tool.Executable + ".exe");

        if (!File.Exists(targetFile))
        {
            using var client = new HttpClient();
            var data = await client.GetByteArrayAsync(downloadUrl, cancellationToken);
            await File.WriteAllBytesAsync(targetFile, data, cancellationToken);
        }

        tool.InstallPath = targetFile;
        tool.IsInstalled = true;
        tool.Version = "latest";
        tool.LastCheckedUtc = DateTimeOffset.UtcNow;

        settings.Current.Tools[id] = tool;
        settings.Save();
    }

    public async Task UpdateAsync(string id, string downloadUrl, CancellationToken cancellationToken = default)
    {
        await EnsureInstalledAsync(id, downloadUrl, cancellationToken);
    }

    public Task RemoveAsync(string id)
    {
        var tool = GetTool(id);
        if (tool is not null && !string.IsNullOrWhiteSpace(tool.InstallPath) && File.Exists(tool.InstallPath))
            File.Delete(tool.InstallPath);

        settings.Current.Tools.Remove(id);
        settings.Save();

        return Task.CompletedTask;
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
    public async Task<string?> ReadVersionAsync(string path, string argument, CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo(path, argument)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
                return null;

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return ParseVersion(output);
        }
        catch (Win32Exception)
        {
            // The file is there but isn't a program that can be run.
            return null;
        }
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
