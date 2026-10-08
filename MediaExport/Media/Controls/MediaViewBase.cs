// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Converters;
using MediaExport.Core.Media.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Linq;

namespace MediaExport.Media.Controls;

/// <summary>What every view of a piece of media shows of it, and the one rule for the shape of its picture.</summary>
public abstract partial class MediaViewBase : UserControl
{
    public static readonly DependencyProperty MediaProperty = DependencyProperty.Register(
        nameof(Media),
        typeof(MediaSearchResult),
        typeof(MediaViewBase),
        new PropertyMetadata(null, (view, _) => ((MediaViewBase)view).RefreshMediaBindings()));

    public MediaSearchResult? Media
    {
        get => (MediaSearchResult?)GetValue(MediaProperty);
        set => SetValue(MediaProperty, value);
    }

    public bool IsAudio => Media?.Kind == MediaKind.Audio;

    /// <summary>Its picture's width over its height.</summary>
    public double Shape => ShapeOf(Media?.Kind ?? MediaKind.Video);

    public string TitleText => Media?.Title ?? string.Empty;
    public string CreatorText => Media?.Creator ?? string.Empty;
    public string? ProviderId => Media?.ProviderId;

    /// <summary>How played and how old, in short ("1.9M plays · 2 years ago").</summary>
    public string MetaText => Format.Meta(Media?.Views, Media?.Published);

    /// <summary>The same exactly, for a tooltip; nothing if neither is known.</summary>
    public string? MetaDetail => Format.MetaDetail(Media?.Views, Media?.Published, Media?.IsDateApproximate == true) is { Length: > 0 } detail ? detail : null;

    /// <summary>Who made it and how played and old it is, on one line for where there's only room for one.</summary>
    public string BylineText => string.Join("  ·  ", new[] { CreatorText, MetaText }.Where(part => part.Length > 0));

    public string AccessibleName => Media?.ToString() ?? string.Empty;
    public ImageSource? ThumbnailSource => Media?.ThumbnailUri is Uri uri ? new BitmapImage(uri) : null;
    public string DurationText => Format.Duration(Media?.Duration);
    public Visibility DurationVisibility => Media?.Duration is null ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Sound comes with a square cover and pictures with a 16:9 frame, so that's the shape each is shown in.</summary>
    public static double ShapeOf(MediaKind kind) => kind == MediaKind.Audio ? 1 : 16.0 / 9;

    /// <summary>How wide a thumbnail of a given height is, for its kind of media.</summary>
    public static double WidthFor(MediaKind kind, double height) => Math.Round(ShapeOf(kind) * height);

    protected abstract void RefreshMediaBindings();
}
