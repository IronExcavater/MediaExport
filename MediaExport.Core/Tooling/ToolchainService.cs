// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

namespace MediaExport.Core.Tooling;

/// <summary>Finds, installs and updates the external programs the app runs (yt-dlp, ffmpeg).</summary>
/// <param name="folder">The folder holding the copies the app installs itself.</param>
public sealed class ToolchainService(string folder)
{
    public string Folder { get; } = folder;

    /// <returns>The full path of a program, e.g. <c>yt-dlp.exe</c>, or <c>null</c> if it isn't installed.</returns>
    public string? Locate(string executable) => throw new NotImplementedException();
}
