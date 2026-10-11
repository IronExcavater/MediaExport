// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using System;
using System.Collections.Generic;
using System.Globalization;

namespace MediaExport.Converters;

/// <summary>Writes times, counts and dates the way the app shows them. Usable from XAML with <c>{x:Bind}</c>.</summary>
public static class Format
{
    /// <summary><c>3:07</c>, or <c>1:03:07</c> from an hour up.</summary>
    public static string Time(TimeSpan time)
    {
        var format = time.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss";
        return time.ToString(format, CultureInfo.InvariantCulture);
    }

    /// <summary>A count written short: <c>812</c>, <c>4.2K</c>, <c>1.9M</c>, <c>2.8B</c>.</summary>
    public static string Count(long count) => count switch
    {
        >= 1_000_000_000 => $"{count / 1e9:0.#}B",
        >= 1_000_000 => $"{count / 1e6:0.#}M",
        >= 1_000 => $"{count / 1e3:0.#}K",
        _ => count.ToString(CultureInfo.InvariantCulture)
    };

    /// <summary>How long ago, in the largest unit that fits: <c>today</c>, <c>3 days ago</c>, <c>2 years ago</c>.</summary>
    public static string Age(DateTimeOffset then, DateTimeOffset now)
    {
        var days = (now - then).TotalDays;
        (var amount, var unit) = days switch
        {
            < 1 => (0, "day"),
            < 30 => (days, "day"),
            < 365 => (days / 30, "month"),
            _ => (days / 365, "year")
        };

        var whole = (int)amount;
        if (whole == 0)
            return "today";

        return whole == 1 ? $"1 {unit} ago" : $"{whole} {unit}s ago";
    }

    /// <summary><see cref="Age(DateTimeOffset, DateTimeOffset)"/> as of now; empty when the date isn't known.</summary>
    public static string Age(DateTimeOffset? then) => then is null ? string.Empty : Age(then.Value, DateTimeOffset.Now);

    /// <summary>How often something was viewed: <c>1 view</c>, <c>1.9M views</c>; empty when it isn't known.</summary>
    public static string Views(long? views)
    {
        if (views is null)
            return string.Empty;

        var unit = views == 1 ? "view" : "views";
        return $"{Count(views.Value)} {unit}";
    }
}
