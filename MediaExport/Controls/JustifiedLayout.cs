// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using Windows.Foundation;

namespace MediaExport.Controls;

/// <summary>Rows of items whose pictures are all one height and each as wide as its own shape, as in a photo gallery.</summary>
/// <remarks>
/// About as many items go in a row as fit, and a full row is then scaled a little so that it ends at the edge.
/// Every item is measured on each pass, so this suits tens of items rather than thousands.
/// </remarks>
public sealed partial class JustifiedLayout : NonVirtualizingLayout
{
    /// <summary>How wide an item's picture is for each unit of its height. It is 16:9 on an item that doesn't say.</summary>
    public static readonly DependencyProperty ShapeProperty = DependencyProperty.RegisterAttached(
        "Shape",
        typeof(double),
        typeof(JustifiedLayout),
        new PropertyMetadata(16.0 / 9));

    // Where each item goes, worked out while measuring and used when arranging.
    private readonly List<Rect> _slots = [];

    /// <summary>How tall the pictures are before a row is scaled to fit.</summary>
    public double RowHeight { get; set; } = 150;

    /// <summary>The width an item has besides its picture, such as its padding.</summary>
    public double ItemPadding { get; set; }

    public double RowSpacing { get; set; }

    public static double GetShape(DependencyObject item) => (double)item.GetValue(ShapeProperty);

    public static void SetShape(DependencyObject item, double value) => item.SetValue(ShapeProperty, value);

    protected override Size MeasureOverride(NonVirtualizingLayoutContext context, Size availableSize)
    {
        _slots.Clear();
        var children = context.Children;
        var width = double.IsInfinity(availableSize.Width) ? RowHeight * 4 : availableSize.Width;

        double top = 0;
        var rowStart = 0;
        while (rowStart < children.Count)
        {
            // As many items as fit at the usual height, and always at least one.
            var rowEnd = rowStart;
            double shapes = 0;
            while (rowEnd < children.Count)
            {
                var shape = GetShape(children[rowEnd]);
                var count = rowEnd - rowStart + 1;
                var widthWithIt = (shapes + shape) * RowHeight + count * ItemPadding;
                var isFirst = rowEnd == rowStart;
                if (!isFirst && widthWithIt > width)
                    break;

                shapes += shape;
                rowEnd++;
            }

            // One more item, shrunk a little, if that leaves the pictures nearer their usual height than stretching
            // these to the edge would.
            if (rowEnd < children.Count)
            {
                var shapesWithNext = shapes + GetShape(children[rowEnd]);
                var stretch = ScaleToFit(width, rowEnd - rowStart, shapes) - 1;
                var shrink = 1 - ScaleToFit(width, rowEnd - rowStart + 1, shapesWithNext);
                if (shrink < stretch)
                {
                    shapes = shapesWithNext;
                    rowEnd++;
                }
            }

            // Every row follows the same width policy, including the final row, so no trailing row ends early.
            var scale = ScaleToFit(width, rowEnd - rowStart, shapes);
            var pictureHeight = RowHeight * scale;

            double left = 0;
            double rowHeight = 0;
            for (var index = rowStart; index < rowEnd; index++)
            {
                var itemWidth = GetShape(children[index]) * pictureHeight + ItemPadding;
                children[index].Measure(new Size(itemWidth, double.PositiveInfinity));
                rowHeight = Math.Max(rowHeight, children[index].DesiredSize.Height);
                _slots.Add(new Rect(left, top, itemWidth, 0));
                left += itemWidth;
            }

            // Every item in a row is as tall as the tallest of them.
            for (var index = rowStart; index < rowEnd; index++)
                _slots[index] = _slots[index] with { Height = rowHeight };

            top += rowHeight + RowSpacing;
            rowStart = rowEnd;
        }

        var height = Math.Max(0, top - RowSpacing);
        return new Size(width, height);
    }

    // How much taller or shorter than usual a row's pictures must be for the row to end at the edge.
    private double ScaleToFit(double width, int itemCount, double shapes) =>
        (width - itemCount * ItemPadding) / (shapes * RowHeight);

    protected override Size ArrangeOverride(NonVirtualizingLayoutContext context, Size finalSize)
    {
        var children = context.Children;
        for (var index = 0; index < children.Count; index++)
            children[index].Arrange(_slots[index]);

        return finalSize;
    }
}
