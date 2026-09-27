// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using System;
using System.Globalization;

namespace MediaExport.Converters;

/// <summary>Times and sizes as the app writes them, for code and x:Bind alike.</summary>
public static class Format
{
    /// <summary><c>3:07</c>, or <c>1:03:07</c> from an hour up.</summary>
    public static string Time(TimeSpan time) =>
        time.ToString(time.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture);

    /// <summary>A length if it's known, otherwise nothing.</summary>
    public static string Duration(TimeSpan? duration) => duration is { } time ? Time(time) : string.Empty;

    public static string Size(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0} MB",
        _ => $"{Math.Max(1, bytes / 1024)} KB"
    };
}
