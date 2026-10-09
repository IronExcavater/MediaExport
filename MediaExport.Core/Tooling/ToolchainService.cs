// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

namespace MediaExport.Core.Tooling;

/// <summary>Finds, installs and updates the external programs the app runs (yt-dlp, ffmpeg).</summary>
/// <param name="folder">The folder holding the copies the app installs itself.</param>
public sealed class ToolchainService(string folder)
{
    public string Folder { get; } = folder;

    /// <summary>Finds a program by name, e.g. <c>yt-dlp</c>: the app's own copy if it has one, otherwise one on PATH.</summary>
    /// <returns>The program's full path, or <c>null</c> if it isn't installed.</returns>
    public string? Locate(string executable)
    {
        var fileName = executable + ".exe";
        var searchPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var folders = searchPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var candidateFolder in folders.Prepend(Folder))
        {
            var candidate = Path.Combine(candidateFolder, fileName);
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }
}
