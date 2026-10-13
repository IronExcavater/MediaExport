// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Xaml;

namespace MediaExport.Converters;

/// <summary><see cref="Is"/>'s questions answered as a <c>Visibility</c>, for <c>{x:Bind}</c>.</summary>
/// <remarks>{x:Bind} skips a function given null, so <see cref="NotNull"/> needs FallbackValue=Collapsed.</remarks>
public static class VisibleWhen
{
    public static Visibility NotNull(object? value) => True(Is.NotNull(value));

    public static Visibility NotBlank(string? text) => True(Is.NotBlank(text));

    public static Visibility True(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility False(bool value) => True(!value);
}
