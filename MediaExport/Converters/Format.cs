// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using System;
using System.Globalization;
using System.Linq;

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
        double days = (now - then).TotalDays;
        (double amount, string unit) = days switch
        {
            < 1 => (0, "day"),
            < 30 => (days, "day"),
            < 365 => (days / 30, "month"),
            _ => (days / 365, "year")
        };
        int whole = (int)amount;
        return whole == 0 ? "today" : whole == 1 ? $"1 {unit} ago" : $"{whole} {unit}s ago";
    }

    /// <summary>What's known of how often something was viewed and when it went up: <c>1.9M views · 2 years ago</c>.</summary>
    public static string Meta(long? views, DateTimeOffset? published) => string.Join("  ·  ", new[]
    {
        views is { } count ? $"{Count(count)} {(count == 1 ? "view" : "views")}" : null,
        published is { } then ? Age(then, DateTimeOffset.Now) : null
    }.Where(part => part is not null));

    /// <summary>
    /// The same in full, for a tooltip: <c>21,315,080 views · 17 April 2013</c>. A date known only roughly is given as its year.
    /// </summary>
    public static string MetaDetail(long? views, DateTimeOffset? published, bool isDateApproximate) => string.Join("  ·  ", new[]
    {
        views is { } count ? $"{count.ToString("N0", CultureInfo.CurrentCulture)} {(count == 1 ? "view" : "views")}" : null,
        published is not { } then ? null
            : isDateApproximate ? $"around {then.LocalDateTime.Year}"
            : then.LocalDateTime.ToString("d MMMM yyyy", CultureInfo.CurrentCulture)
    }.Where(part => part is not null));

    /// <summary>Time left, rounded to what's worth saying: <c>about 20 seconds left</c>, <c>about 3 minutes left</c>.</summary>
    public static string Remaining(TimeSpan left) => left.TotalSeconds switch
    {
        < 10 => "a few seconds left",
        < 60 => $"about {Math.Round(left.TotalSeconds / 10) * 10:0} seconds left",
        < 90 => "about a minute left",
        < 3600 => $"about {Math.Round(left.TotalMinutes):0} minutes left",
        _ => $"about {left.TotalHours:0.#} hours left"
    };
}
