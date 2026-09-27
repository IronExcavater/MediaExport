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
/// Missing icons show a generic globe, so a new source works before it has one.
/// </summary>
public sealed partial class IconView : Grid
{
    private static readonly string[] Extensions = [".svg", ".png", ".jpg", ".jpeg", ".ico", ".bmp", ".gif"];

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(string), typeof(IconView), new PropertyMetadata(null, (view, _) => ((IconView)view).Refresh()));

    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(string), typeof(IconView), new PropertyMetadata(null, (view, _) => ((IconView)view).Refresh()));

    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly FontIcon _fallback = new() { Glyph = "" };

    public IconView()
    {
        Children.Add(_fallback);
        Children.Add(_image);
        ActualThemeChanged += (_, _) => Refresh();
        SizeChanged += (_, args) => _fallback.FontSize = Math.Max(8, Math.Min(args.NewSize.Width, args.NewSize.Height) * 0.8);
    }

    /// <summary>The media sources' icons by source id, set up once at start.</summary>
    public static IReadOnlyDictionary<string, string> SourceIcons { get; set; } = new Dictionary<string, string>();

    /// <summary>An icon's name, e.g. <c>internet-archive</c>.</summary>
    public string? Icon
    {
        get => (string?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>A media source's id, e.g. <c>youtube</c>, standing in for its icon's name.</summary>
    public string? Source
    {
        get => (string?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    private void Refresh()
    {
        string? name = Icon ?? (Source is { } source ? SourceIcons.GetValueOrDefault(source) : null);
        string? file = Find(name, ActualTheme == ElementTheme.Dark ? "dark" : "light");
        _image.Source = file is null ? null
            : file.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ? new SvgImageSource(ToUri(file))
            : new BitmapImage(ToUri(file));
        _fallback.Visibility = file is null ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The icon's file for this theme, or its theme-less file, in the first format found.</summary>
    internal static string? Find(string? name, string theme)
    {
        if (string.IsNullOrEmpty(name))
            return null;

        string folder = Path.Combine(AppContext.BaseDirectory, "Assets", "Icons");
        return new[] { $"{name}.{theme}", name }
            .SelectMany(stem => Extensions.Select(extension => stem + extension))
            .FirstOrDefault(file => File.Exists(Path.Combine(folder, file)));
    }

    private static Uri ToUri(string file) => new($"ms-appx:///Assets/Icons/{file}");
}
