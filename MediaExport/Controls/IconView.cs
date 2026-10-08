// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MediaExport.Controls;

/// <summary>
/// An icon from Assets/Icons, in whatever image format it was saved as. <c>name.light.*</c> and
/// <c>name.dark.*</c> are used on light and dark backgrounds; plain <c>name.*</c> suits both.
/// </summary>
public sealed class IconView : Grid
{
    private static readonly string[] Extensions = [".svg", ".png", ".jpg", ".jpeg", ".ico"];

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(string), typeof(IconView), new PropertyMetadata(null, OnIconChanged));

    private readonly Image _image = new() { Stretch = Stretch.Uniform };

    public IconView()
    {
        Children.Add(_image);
        ActualThemeChanged += (_, _) => Refresh();
    }

    /// <summary>The media sources' icons by source id, set up once at start.</summary>
    public static IReadOnlyDictionary<string, string> SourceIcons { get; set; } = new Dictionary<string, string>();

    /// <summary>An icon's name or a media provider id, e.g. <c>internet-archive</c> or <c>youtube</c>.</summary>
    public string? Icon
    {
        get => (string?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    private static void OnIconChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((IconView)sender).Refresh();

    private void Refresh()
    {
        string? name = Icon is { } icon ? SourceIcons.GetValueOrDefault(icon, icon) : null;
        Uri? uri = UriFor(name, ActualTheme);
        _image.Source = uri is null ? null
            : uri.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ? new SvgImageSource(uri)
            : new BitmapImage(uri);
        Visibility = uri is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>The packaged SVG for this icon and theme.</summary>
    public static Uri? UriFor(string? name, ElementTheme theme)
    {
        if (string.IsNullOrEmpty(name))
            return null;

        string folder = Path.Combine(AppContext.BaseDirectory, "Assets", "Icons");
        string appearance = theme == ElementTheme.Dark ? "dark" : "light";
        string? file = new[] { $"{name}.{appearance}", name }
            .SelectMany(stem => Extensions.Select(extension => stem + extension))
            .FirstOrDefault(candidate => File.Exists(Path.Combine(folder, candidate)));
        return file is null ? null : new Uri($"ms-appx:///Assets/Icons/{file}");
    }
}
