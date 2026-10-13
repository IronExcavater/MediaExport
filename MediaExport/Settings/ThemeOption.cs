// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

namespace MediaExport.Settings;

public sealed class ThemeOption
{
    public string Name { get; set; } = "";
    public string Glyph { get; set; } = "";

    // Screen readers announce a list item by its ToString.
    public override string ToString() => Name;
}
