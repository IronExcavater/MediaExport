// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Xaml;

namespace MediaExport.Converters;

/// <summary>
/// The questions in <see cref="Is"/>, answered as a <c>Visibility</c>:
/// <code>
/// Visibility="{x:Bind converters:VisibleWhen.NotNull(Media.Duration), Mode=OneWay}"
/// </code>
/// </summary>
/// <remarks>
/// <c>{x:Bind}</c> turns a bool property into a Visibility by itself, but not the bool a function returns:
/// the XAML compiler generates code for that which doesn't compile. So a Visibility bound to a function
/// uses one of these, and anything else uses <see cref="Is"/>.
/// </remarks>
public static class VisibleWhen
{
    public static Visibility Null(object? value) => True(Is.Null(value));

    public static Visibility NotNull(object? value) => True(Is.NotNull(value));

    public static Visibility NotBlank(string? text) => True(Is.NotBlank(text));

    public static Visibility False(bool value) => True(!value);

    public static Visibility True(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
}
