// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;

namespace MediaExport.Media.Controls;

public abstract class MediaViewBase : UserControl
{
    public static readonly DependencyProperty MediaProperty = DependencyProperty.Register(
        nameof(Media),
        typeof(MediaSearchResult),
        typeof(MediaViewBase),
        new PropertyMetadata(null, OnMediaChanged));

    public MediaSearchResult? Media
    {
        get => (MediaSearchResult?)GetValue(MediaProperty);
        set => SetValue(MediaProperty, value);
    }

    public string DurationText => Media?.Duration?.ToString(@"m\:ss") ?? string.Empty;
    public ImageSource? ThumbnailSource => Media?.ThumbnailUri is Uri uri ? new BitmapImage(uri) : null;

    protected abstract void RefreshMediaBindings();

    private static void OnMediaChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args) =>
        ((MediaViewBase)dependencyObject).RefreshMediaBindings();
}
