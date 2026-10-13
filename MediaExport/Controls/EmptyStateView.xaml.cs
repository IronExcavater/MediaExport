// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Xaml.Controls;

namespace MediaExport.Controls;

/// <summary>What a page shows in the middle of itself while it has nothing to show: an icon and a line of text.</summary>
/// <remarks>The properties are read once, as the view loads, so they are set in XAML and not changed after.</remarks>
public sealed partial class EmptyStateView : UserControl
{
    public EmptyStateView()
    {
        InitializeComponent();
    }

    /// <summary>The icon, as a character of the icon font.</summary>
    public string Glyph { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    /// <summary>What can be done about there being nothing, such as a button. Shown under the title.</summary>
    public object? Action { get; set; }
}
