// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.IO;

namespace MediaExport.Converters;

// Maps an icon name to Assets/Icons/{name}.svg, or nothing if there's no such icon yet.
public sealed partial class IconImageConverter : IValueConverter
{
    public static bool HasIcon(string? name) =>
        name is not null && File.Exists(Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", name + ".svg"));

    public static SvgImageSource? GetIcon(string? name) =>
        HasIcon(name) ? new SvgImageSource(new Uri($"ms-appx:///Assets/Icons/{name}.svg")) : null;

    /// <summary>A generic web icon stands in for sources without their own.</summary>
    public static Visibility ShowFallback(string? name) => HasIcon(name) ? Visibility.Collapsed : Visibility.Visible;

    public object? Convert(object value, Type targetType, object parameter, string language) =>
        GetIcon(value as string);

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}
