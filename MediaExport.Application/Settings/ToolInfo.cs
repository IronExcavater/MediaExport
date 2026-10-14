// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

namespace MediaExport.Settings;

/// <summary>A program MediaExport depends on and the links used to inspect or obtain it.</summary>
public sealed record ToolInfo(
    string Id,
    string Name,
    string VersionArgument,
    Uri Website,
    Uri SourceCode,
    string? DownloadUrl);
