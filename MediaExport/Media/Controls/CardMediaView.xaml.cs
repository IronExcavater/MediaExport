// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

namespace MediaExport.Media.Controls;

/// <summary>Shows one media result as a card, for grids such as a page of search results.</summary>
public sealed partial class CardMediaView : MediaView
{
    public CardMediaView()
    {
        InitializeComponent();
    }

    /// <summary>The shape of the card's picture while it is a skeleton, with no media to take it from.</summary>
    public double PlaceholderShape
    {
        get => thumbnail.PlaceholderShape;
        set => thumbnail.PlaceholderShape = value;
    }
}
