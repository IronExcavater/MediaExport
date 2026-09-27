// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Xaml.Data;
using System;

namespace MediaExport.Converters;

/// <summary>
/// Lets x:Bind two-way bind a selector's SelectedItem (an object) to a typed property:
/// the value is passed through unchanged, and x:Bind casts it on the way back.
/// </summary>
public sealed partial class PassThroughConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object parameter, string language) => value;

    public object? ConvertBack(object? value, Type targetType, object parameter, string language) => value;
}
