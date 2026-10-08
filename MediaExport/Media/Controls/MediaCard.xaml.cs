// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using Microsoft.UI.Xaml;
using System;

namespace MediaExport.Media.Controls;

/// <summary>
/// Media as a card to pick from a feed: its picture, then what it is, where it's from and who made it.
/// The one way media is shown in a grid, on the Search page and the home page alike.
/// </summary>
public sealed partial class MediaCard : MediaViewBase
{
    /// <summary>The room a card takes beside its picture (its padding), and beneath it (padding, gap and words).</summary>
    public const double ChromeWidth = 16, ChromeHeight = 16 + 10 + 84;

    public MediaCard()
    {
        InitializeComponent();
    }

    /// <summary>The card was clicked.</summary>
    public event EventHandler<MediaSearchResult>? Chosen;

    protected override void RefreshMediaBindings()
    {
        Bindings.Update();
        // Its shape may have changed with its media, and with that how wide it should be.
        InvalidateMeasure();
    }

    private void Button_Click(object sender, RoutedEventArgs args)
    {
        if (Media is { } media)
            Chosen?.Invoke(this, media);
    }
}
