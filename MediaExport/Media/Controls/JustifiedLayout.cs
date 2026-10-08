// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Foundation;

namespace MediaExport.Media.Controls;

/// <summary>
/// Lays <see cref="MediaCard"/>s out in rows where every picture in a row is the same height and as wide as
/// its own shape: 16:9 for video, square for a cover. Each full row is scaled a little so it ends flush with
/// the edge, the way photo galleries do, so shapes can be mixed without cropping or padding any of them.
/// </summary>
public sealed partial class JustifiedLayout : NonVirtualizingLayout
{
    // A full row grows at most this much to reach the edge; past that it's left short instead.
    private const double MostStretch = 1.3;

    private readonly List<Rect> _places = [];

    /// <summary>How tall pictures are before a row is scaled to fit.</summary>
    public double RowHeight { get; set; } = 180;

    public double Spacing { get; set; } = 4;

    protected override Size MeasureOverride(NonVirtualizingLayoutContext context, Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? 1200 : availableSize.Width;
        double[] shapes = context.Children.Select(child => (child as MediaCard)?.Shape ?? 16.0 / 9).ToArray();
        _places.Clear();

        double top = 0;
        for (int start = 0; start < shapes.Length;)
        {
            // As many as fit at the usual height, and always at least one.
            int count = 1;
            while (start + count < shapes.Length && NaturalWidth(shapes, start, count + 1) <= width)
                count++;

            // One more, shrunk a little, if that's nearer the usual height than stretching these to the edge.
            if (start + count < shapes.Length && 1 - Scale(shapes, start, count + 1, width) < Scale(shapes, start, count, width) - 1)
                count++;

            bool isLastRow = start + count == shapes.Length;
            double scale = Scale(shapes, start, count, width);
            // The last row isn't stretched to fill: a lone card would balloon.
            double height = RowHeight * (isLastRow ? Math.Min(1, scale) : Math.Min(MostStretch, scale));

            double left = 0;
            for (int index = start; index < start + count; index++)
            {
                var place = new Rect(left, top, Math.Floor(shapes[index] * height) + MediaCard.ChromeWidth, Math.Floor(height) + MediaCard.ChromeHeight);
                _places.Add(place);
                context.Children[index].Measure(new Size(place.Width, place.Height));
                left += place.Width + Spacing;
            }

            top += Math.Floor(height) + MediaCard.ChromeHeight + Spacing;
            start += count;
        }

        return new Size(width, Math.Max(0, top - Spacing));
    }

    protected override Size ArrangeOverride(NonVirtualizingLayoutContext context, Size finalSize)
    {
        for (int index = 0; index < Math.Min(_places.Count, context.Children.Count); index++)
            context.Children[index].Arrange(_places[index]);
        return finalSize;
    }

    // How much taller or shorter than usual a row's pictures must be for it to end at the edge.
    private double Scale(double[] shapes, int start, int count, double width) =>
        (width - count * MediaCard.ChromeWidth - (count - 1) * Spacing) / (shapes.Skip(start).Take(count).Sum() * RowHeight);

    private double NaturalWidth(double[] shapes, int start, int count) =>
        shapes.Skip(start).Take(count).Sum() * RowHeight + count * MediaCard.ChromeWidth + (count - 1) * Spacing;
}
