// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Providers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System;

namespace MediaExport.Media.Controls;

public sealed partial class ProviderIconView : UserControl
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon),
        typeof(MediaProviderIcon),
        typeof(ProviderIconView),
        new PropertyMetadata(MediaProviderIcon.None, OnIconChanged));

    public MediaProviderIcon Icon
    {
        get => (MediaProviderIcon)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public ProviderIconView()
    {
        InitializeComponent();
        youTubeMark.Source = new SvgImageSource(new Uri("ms-appx:///Assets/Providers/YoutubeIconRed.svg"));
        soundCloudMark.Source = new SvgImageSource(new Uri("ms-appx:///Assets/Providers/SoundCloudOrange.svg"));
        UpdateIcon();
    }

    private static void OnIconChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((ProviderIconView)sender).UpdateIcon();

    private void UpdateIcon()
    {
        youTubeMark.Visibility = Icon is MediaProviderIcon.YouTube ? Visibility.Visible : Visibility.Collapsed;
        soundCloudMark.Visibility = Icon is MediaProviderIcon.SoundCloud ? Visibility.Visible : Visibility.Collapsed;
    }
}
