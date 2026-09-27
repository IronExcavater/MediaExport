// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Xaml;
using System;

namespace MediaExport.Converters;

/// <summary>Visibility helpers for x:Bind, which turns a plain bool into Visibility but not its opposite.</summary>
public static class Show
{
    public static bool Not(bool value) => !value;

    public static Visibility Unless(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility When(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility WhenText(string? value) => string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility WhenSet(TimeSpan? value) => value is null ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility WhenAll(bool first, bool second) => first && second ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility WhenNone(bool first, bool second) => !first && !second ? Visibility.Visible : Visibility.Collapsed;
}
