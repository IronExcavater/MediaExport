// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;
using System;

namespace MediaExport.Converters;

// Maps an icon name to Assets/Icons/{name}.svg.
public sealed partial class IconImageConverter : IValueConverter
{
    public static SvgImageSource? GetIcon(string? name) =>
        name is null ? null : new SvgImageSource(new Uri($"ms-appx:///Assets/Icons/{name}.svg"));

    public object? Convert(object value, Type targetType, object parameter, string language) =>
        GetIcon(value as string);

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}
