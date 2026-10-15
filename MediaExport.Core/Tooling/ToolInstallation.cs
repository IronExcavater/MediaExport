// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

namespace MediaExport.Core.Tooling;

public enum ToolOrigin
{
    Managed,
    External
}

public sealed record ToolInstallation(string Path, ToolOrigin Origin);
