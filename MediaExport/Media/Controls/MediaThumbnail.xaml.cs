// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Converters;
using MediaExport.Core.Media.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using Windows.Foundation;

namespace MediaExport.Media.Controls;

/// <summary>A media result's thumbnail, with its duration over it, sized to its picture's shape.</summary>
public sealed partial class MediaThumbnail : MediaView
{
    // Wide enough for the largest size a thumbnail is shown at, on a sharp display.
    private const int DecodeWidth = 480;

    public MediaThumbnail()
    {
        InitializeComponent();
    }

    /// <summary>How wide a kind of media's picture is for each unit of its height.</summary>
    /// <remarks>Audio comes with square cover art, and video thumbnails are 16:9.</remarks>
    public static double ShapeOf(MediaKind kind) => kind == MediaKind.Audio ? 1 : 16.0 / 9;

    /// <summary>The shape to be while there is no media to take it from, as a skeleton is.</summary>
    public double PlaceholderShape
    {
        get;
        set
        {
            field = value;
            InvalidateMeasure();
        }
    } = ShapeOf(MediaKind.Video);

    private double AspectRatio => Media is null ? PlaceholderShape : ShapeOf(Media.Kind);

    protected override void OnMediaChanged(MediaSearchResult? oldMedia, MediaSearchResult? newMedia)
    {
        // The shape depends only on the kind of media.
        if (oldMedia?.Kind != newMedia?.Kind)
            InvalidateMeasure();

        // Fetched only when the picture differs, and cleared so a reused row doesn't show the previous media's picture.
        var thumbnailUri = newMedia?.ThumbnailUri;
        if (oldMedia?.ThumbnailUri != thumbnailUri)
        {
            var picture = thumbnailUri is null ? null : new BitmapImage(thumbnailUri);
            picture?.DecodePixelWidth = DecodeWidth;
            image.Source = picture;
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // A row fixes the height and leaves the width open; anything else fixes the width.
        var isHeightGiven = double.IsInfinity(availableSize.Width) && !double.IsInfinity(availableSize.Height);
        var size = isHeightGiven
            ? new Size(availableSize.Height * AspectRatio, availableSize.Height)
            : new Size(availableSize.Width, availableSize.Width / AspectRatio);

        base.MeasureOverride(size);
        return size;
    }

    private string DurationText(TimeSpan? duration) => duration is null ? string.Empty : Format.Time(duration.Value);

}
