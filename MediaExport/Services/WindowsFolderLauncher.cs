// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Settings;
using System.Diagnostics;

namespace MediaExport.Services;

public sealed class WindowsFolderLauncher : IFolderLauncher
{
    public void Open(string folder)
    {
        Directory.CreateDirectory(folder);
        Process.Start("explorer.exe", $"\"{folder}\"");
    }
}
