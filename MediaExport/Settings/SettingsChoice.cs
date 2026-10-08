// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

namespace MediaExport.Settings;

/// <summary>A labelled value used by compact settings that do not need richer presentation.</summary>
public sealed record SettingsChoice(string Label, string? Value, string Glyph)
{
    public override string ToString() => Label;
}
