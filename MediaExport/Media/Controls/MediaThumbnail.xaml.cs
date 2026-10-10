// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Converters;
using MediaExport.Core.Media.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using Windows.Foundation;

namespace MediaExport.Media.Controls;

/// <summary>
/// A media result's thumbnail, with its duration over it. It keeps the right shape for its media by itself:
/// give it a height and it works out its width, or let it stretch across and it works out its height.
/// </summary>
public sealed partial class MediaThumbnail : MediaView
{
    // Wide enough for the largest size a thumbnail is shown at, on a sharp display.
    private const int DecodeWidth = 480;

    public MediaThumbnail()
    {
        InitializeComponent();
    }

    // Audio comes with square cover art; video thumbnails are 16:9.
    private double AspectRatio => Media?.Kind == MediaKind.Audio ? 1 : 16.0 / 9;

    protected override void OnMediaChanged(MediaSearchResult? oldMedia, MediaSearchResult? newMedia)
    {
        // The shape depends only on the kind of media.
        if (oldMedia?.Kind != newMedia?.Kind)
            InvalidateMeasure();

        // The picture is only fetched again when it is a different picture. Clearing it for media without
        // one also stops a reused row showing the previous media's picture.
        var thumbnailUri = newMedia?.ThumbnailUri;
        if (oldMedia?.ThumbnailUri != thumbnailUri)
            image.Source = thumbnailUri is null ? null : new BitmapImage(thumbnailUri) { DecodePixelWidth = DecodeWidth };
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

    private Visibility VisibleWith(TimeSpan? duration) => duration is null ? Visibility.Collapsed : Visibility.Visible;
}
