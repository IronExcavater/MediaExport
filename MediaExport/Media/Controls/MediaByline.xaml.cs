// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Converters;
using MediaExport.Core.Media.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using Windows.Foundation;

namespace MediaExport.Media.Controls;

/// <summary>The line under a media result's title: its provider's icon, who made it, its views and its age.</summary>
public sealed partial class MediaByline : MediaView
{
    public MediaByline()
    {
        InitializeComponent();
    }

    // The room on either side of a dot.
    private const double Gap = 3;

    private bool _hasCreator;
    private bool _hasViews;
    private bool _hasAge;

    protected override void OnMediaChanged(MediaSearchResult? oldMedia, MediaSearchResult? newMedia)
    {
        _hasCreator = !string.IsNullOrEmpty(newMedia?.Creator);
        _hasViews = newMedia?.Views is not null;
        _hasAge = newMedia?.Published is not null;
        InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var unlimited = new Size(double.PositiveInfinity, double.PositiveInfinity);
        double left = 0;
        double top = 0;
        double lineHeight = 0;
        double width = 0;

        // A name too long for a line of its own is cut short.
        creatorButton.MaxWidth = availableSize.Width;
        viewsSeparator.Opacity = 0;
        ageSeparator.Opacity = 0;

        // A dot only goes between two values, and an unknown value has no text to place.
        Place(creatorButton, separator: null);
        Place(viewsText, _hasCreator ? viewsSeparator : null);
        Place(ageText, _hasCreator || _hasViews ? ageSeparator : null);

        base.MeasureOverride(availableSize);
        return new Size(Math.Min(width, availableSize.Width), top + lineHeight);

        void Place(FrameworkElement part, TextBlock? separator)
        {
            part.Measure(unlimited);
            separator?.Measure(unlimited);
            var partWidth = part.DesiredSize.Width;
            if (partWidth == 0)
                return;

            // A part that doesn't fit beside the one before it starts a new line, without the dot between them.
            var isLineStart = left == 0;
            var separatorWidth = separator is null ? Gap : Gap + separator.DesiredSize.Width + Gap;
            var fits = left + separatorWidth + partWidth <= availableSize.Width;
            if (!isLineStart && !fits)
            {
                left = 0;
                top += lineHeight;
                isLineStart = true;
            }

            if (!isLineStart)
            {
                if (separator is not null)
                {
                    separator.Opacity = 1;
                    Canvas.SetLeft(separator, left + Gap);
                    Canvas.SetTop(separator, top);
                }
                left += separatorWidth;
            }

            Canvas.SetLeft(part, left);
            Canvas.SetTop(part, top);
            left += partWidth;
            lineHeight = Math.Max(lineHeight, part.DesiredSize.Height);
            width = Math.Max(width, left);
        }
    }
}
