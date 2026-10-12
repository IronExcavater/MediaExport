// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Converters;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

namespace MediaExport.Export.Controls;

/// <summary>One recently viewed media in the navigation pane: its source's icon, its title and a remove button.</summary>
public sealed partial class ViewedMediaRow : UserControl
{
    /// <param name="providerIcon">The resource key of the icon of the source the media is from.</param>
    public ViewedMediaRow(ViewedMedia viewed, string? providerIcon)
    {
        InitializeComponent();
        Viewed = viewed;
        icon.Icon = providerIcon;
        title.Text = viewed.Media.Title;
        IsPointerOver = false;
    }

    public ViewedMedia Viewed { get; }

    public event EventHandler? RemoveRequested;

    /// <summary>
    /// Whether the pointer is over the pane item the row is in, which is larger than the row and is what says so.
    /// </summary>
    public bool IsPointerOver
    {
        set
        {
            removeButton.Visibility = VisibleWhen.True(value);
            pinMark.Visibility = VisibleWhen.True(Viewed.IsPinned && !value);
        }
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs args) => RemoveRequested?.Invoke(this, EventArgs.Empty);
}
