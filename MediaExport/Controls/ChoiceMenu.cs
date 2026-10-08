// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Xaml.Controls;

namespace MediaExport.Controls;

/// <summary>Builds the app's icon-led menu choices with one consistent selected treatment.</summary>
public static class ChoiceMenu
{
    public static MenuFlyoutItem Header(string text) => new()
    {
        Text = text,
        IsEnabled = false
    };

    public static ToggleMenuFlyoutItem Single(string text, object tag, string glyph, bool isChecked) =>
        Single(text, tag, new FontIcon { Glyph = glyph, FontSize = 14 }, isChecked);

    public static ToggleMenuFlyoutItem Single(string text, object tag, IconElement? icon, bool isChecked) => new()
    {
        Text = text,
        Tag = tag,
        Icon = icon,
        IsChecked = isChecked
    };

    public static ToggleMenuFlyoutItem Toggle(string text, object tag, string glyph, bool isChecked) =>
        Single(text, tag, glyph, isChecked);
}
