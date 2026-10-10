// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using Windows.Foundation;

namespace MediaExport.Controls;

/// <summary>
/// Lays items out in columns of equal width, as many as fit, like <see cref="UniformGridLayout"/> with
/// <c>ItemsStretch="Fill"</c>. Unlike it, each row is as tall as its tallest item, and the other items in the
/// row are stretched to that height. UniformGridLayout gives every item the height of the first one.
/// </summary>
/// <remarks>Every item is measured on each pass, so this suits tens of items rather than thousands.</remarks>
public sealed partial class RowGridLayout : NonVirtualizingLayout
{
    // Where each item goes, worked out while measuring and used when arranging.
    private readonly List<Rect> _slots = [];

    /// <summary>The narrowest an item may be. Items are widened from this to share out the spare width.</summary>
    public double MinItemWidth { get; set; } = 240;

    public double MinColumnSpacing { get; set; }

    public double MinRowSpacing { get; set; }

    protected override Size MeasureOverride(NonVirtualizingLayoutContext context, Size availableSize)
    {
        _slots.Clear();
        var children = context.Children;
        var width = double.IsInfinity(availableSize.Width) ? MinItemWidth : availableSize.Width;

        // A column needs its spacing as well as its width, except the last, hence the extra spacing here.
        var columnCount = Math.Max(1, (int)((width + MinColumnSpacing) / (MinItemWidth + MinColumnSpacing)));
        var totalSpacing = (columnCount - 1) * MinColumnSpacing;
        var itemWidth = (width - totalSpacing) / columnCount;

        double top = 0;
        for (var rowStart = 0; rowStart < children.Count; rowStart += columnCount)
        {
            var rowEnd = Math.Min(rowStart + columnCount, children.Count);

            double rowHeight = 0;
            for (var index = rowStart; index < rowEnd; index++)
            {
                children[index].Measure(new Size(itemWidth, double.PositiveInfinity));
                rowHeight = Math.Max(rowHeight, children[index].DesiredSize.Height);
            }

            for (var index = rowStart; index < rowEnd; index++)
            {
                var column = index - rowStart;
                var left = column * (itemWidth + MinColumnSpacing);
                _slots.Add(new Rect(left, top, itemWidth, rowHeight));
            }

            top += rowHeight + MinRowSpacing;
        }

        var height = Math.Max(0, top - MinRowSpacing);
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(NonVirtualizingLayoutContext context, Size finalSize)
    {
        var children = context.Children;
        for (var index = 0; index < children.Count; index++)
            children[index].Arrange(_slots[index]);

        return finalSize;
    }
}
