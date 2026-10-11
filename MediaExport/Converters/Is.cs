// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

namespace MediaExport.Converters;

/// <summary>
/// Yes-or-no questions about a value, for code and for <c>{x:Bind}</c> wherever a bool is wanted:
/// <code>
/// IsEnabled="{x:Bind converters:Is.NotBlank(ViewModel.Query), Mode=OneWay}"
/// </code>
/// A <c>Visibility</c> uses the same questions through <see cref="VisibleWhen"/>.
/// </summary>
public static class Is
{
    public static bool NotNull(object? value) => value is not null;

    /// <summary>Whether text has anything in it other than spaces.</summary>
    public static bool NotBlank(string? text) => !string.IsNullOrWhiteSpace(text);
}
