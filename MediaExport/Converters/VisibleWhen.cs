// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Xaml;

namespace MediaExport.Converters;

/// <summary>
/// Functions for binding an element's <c>Visibility</c>. Each asks the same question as the function of the
/// same name in <see cref="Is"/>, and answers Visible for yes and Collapsed for no:
/// <code>
/// Visibility="{x:Bind converters:VisibleWhen.NotBlank(ViewModel.SearchedQuery), Mode=OneWay}"
/// </code>
/// </summary>
/// <remarks>
/// These exist because <c>{x:Bind}</c> can't use an <see cref="Is"/> function for a Visibility. It turns a
/// bool property into a Visibility, but not a bool that a function returns.
/// <para>
/// <see cref="NotNull"/> needs one more thing in the binding. <c>{x:Bind}</c> skips a function altogether when
/// the value passed to it is null, so NotNull is only ever called with a value and only ever answers Visible.
/// The binding has to say what null means itself, with <c>FallbackValue</c>:
/// </para>
/// <code>
/// Visibility="{x:Bind converters:VisibleWhen.NotNull(Media.Duration), Mode=OneWay, FallbackValue=Collapsed}"
/// </code>
/// </remarks>
public static class VisibleWhen
{
    public static Visibility NotNull(object? value) => True(Is.NotNull(value));

    public static Visibility NotBlank(string? text) => True(Is.NotBlank(text));

    public static Visibility False(bool value) => True(!value);

    public static Visibility True(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
}
