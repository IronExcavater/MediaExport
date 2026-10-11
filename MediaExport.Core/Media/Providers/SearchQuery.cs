// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MediaExport.Core.Media.Providers;

/// <summary>
/// One term of a search, written as <c>label:value</c>, or <c>label:"two words"</c> when the value has a
/// space in it. <c>source:youtube</c> names the provider to search.
/// </summary>
public sealed record SearchTerm(string Label, string Value)
{
    public override string ToString()
    {
        var hasSpace = Value.Any(char.IsWhiteSpace);
        return hasSpace ? $"{Label}:\"{Value}\"" : $"{Label}:{Value}";
    }
}

/// <summary>
/// The value of a range term: its two ends, written <c>3m..10m</c>. Either end may be left out, as in
/// <c>3m..</c> for anything from three minutes up; an end that is left out is empty text.
/// </summary>
public sealed record SearchRange(string Lower, string Upper)
{
    private const string Separator = "..";

    public bool IsEmpty => Lower.Length == 0 && Upper.Length == 0;

    /// <summary>Reads a range as it is written. A value with no <c>..</c> in it is the lower end alone.</summary>
    public static SearchRange Parse(string value)
    {
        var separator = value.IndexOf(Separator, StringComparison.Ordinal);
        if (separator < 0)
            return new SearchRange(value.Trim(), string.Empty);

        var lower = value[..separator];
        var upper = value[(separator + Separator.Length)..];
        return new SearchRange(lower.Trim(), upper.Trim());
    }

    public override string ToString() => $"{Lower}{Separator}{Upper}";
}

/// <summary>
/// A search as it is written in text: its terms, then the words to search for. In
/// <c>source:youtube exact:"lofi beats" study</c> there are two terms, and the text is <c>study</c>.
/// This is the form a search is copied and pasted in.
/// </summary>
/// <param name="Text">The words to search for, without any terms.</param>
public sealed partial record SearchQuery(string Text, IReadOnlyList<SearchTerm> Terms)
{
    /// <summary>The label of the term that names the provider to search.</summary>
    public const string SourceLabel = "source";

    /// <summary>The label of a term for a word or phrase that results must contain as it is written.</summary>
    public const string ExactLabel = "exact";

    /// <summary>The label of a term for a word or phrase that results must not contain.</summary>
    public const string ExcludeLabel = "exclude";

    /// <summary>The label of a term for who made the media.</summary>
    public const string AuthorLabel = "author";

    /// <summary>The label of the term for a range of durations, written as <c>duration:3m..10m</c>.</summary>
    public const string DurationLabel = "duration";

    /// <summary>The label of the term for a range of publish dates, written as <c>published:2024..2025-06</c>.</summary>
    public const string PublishedLabel = "published";

    /// <summary>
    /// Labels for one end of a range by itself, as a shorter way to write it: <c>longer:3m</c> is
    /// <c>duration:3m..</c>. <see cref="WithEnd"/> turns one into its range.
    /// </summary>
    public const string LongerLabel = "longer";
    public const string ShorterLabel = "shorter";
    public const string AfterLabel = "after";
    public const string BeforeLabel = "before";

    public static SearchTerm Source(string providerId) => new(SourceLabel, providerId);

    /// <summary>Whether a label is for a term whose value is a <see cref="SearchRange"/>.</summary>
    public static bool IsRange(string label) => label is DurationLabel or PublishedLabel;

    /// <summary>The label of the range that a label for one end belongs to, or <c>null</c> for any other label.</summary>
    public static string? RangeLabelOf(string endLabel) => endLabel switch
    {
        LongerLabel or ShorterLabel => DurationLabel,
        AfterLabel or BeforeLabel => PublishedLabel,
        _ => null
    };

    /// <summary>
    /// Whether a term's value can be read for its label. A range needs at least one end, each end that is
    /// there readable as a length or a date, and something between the two. Any other term needs any value.
    /// </summary>
    public static bool IsValid(SearchTerm term)
    {
        if (!IsRange(term.Label))
            return !string.IsNullOrWhiteSpace(term.Value);

        var range = SearchRange.Parse(term.Value);
        var lower = Measure(term.Label, range.Lower);
        var upper = Measure(term.Label, range.Upper);
        var isLowerReadable = range.Lower.Length == 0 || lower is not null;
        var isUpperReadable = range.Upper.Length == 0 || upper is not null;
        var leavesRoom = lower is null || upper is null || lower < upper;
        return !range.IsEmpty && isLowerReadable && isUpperReadable && leavesRoom;
    }

    /// <summary>
    /// Sets one end of a range from a term written for that end alone, such as <c>longer:3m</c>. If the new
    /// end leaves nothing between it and the other, it is the one just asked for, so the other end goes.
    /// </summary>
    /// <param name="range">The range term the search already has, or <c>null</c> if it has none.</param>
    public static SearchTerm WithEnd(SearchTerm? range, SearchTerm end)
    {
        var label = RangeLabelOf(end.Label) ?? throw new ArgumentException($"'{end.Label}' isn't an end of a range.", nameof(end));
        var ends = range is null ? new SearchRange(string.Empty, string.Empty) : SearchRange.Parse(range.Value);
        var isLower = end.Label is LongerLabel or AfterLabel;
        var changed = isLower ? ends with { Lower = end.Value } : ends with { Upper = end.Value };

        var term = new SearchTerm(label, changed.ToString());
        if (IsValid(term))
            return term;

        var alone = isLower ? new SearchRange(end.Value, string.Empty) : new SearchRange(string.Empty, end.Value);
        return new SearchTerm(label, alone.ToString());
    }

    /// <summary>Reads a length of time written as <c>90s</c>, <c>3m</c>, <c>1h</c> or <c>1h30m</c>. A bare number is minutes.</summary>
    public static TimeSpan? ReadLength(string value)
    {
        var text = value.Trim();
        if (int.TryParse(text, out var minutes))
            return TimeSpan.FromMinutes(minutes);

        var parts = Length().Match(text);
        if (!parts.Success || text.Length == 0)
            return null;

        var hours = ReadPart(parts, "hours");
        var wholeMinutes = ReadPart(parts, "minutes");
        var seconds = ReadPart(parts, "seconds");
        return new TimeSpan(hours, wholeMinutes, seconds);
    }

    /// <summary>Reads a date written as a year, a month or a day: <c>2024</c>, <c>2024-06</c>, <c>2024-06-01</c>.</summary>
    public static DateTimeOffset? ReadDate(string value)
    {
        string[] formats = ["yyyy", "yyyy-MM", "yyyy-MM-dd"];
        var isDate = DateTimeOffset.TryParseExact(value.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date);
        return isDate ? date : null;
    }

    /// <summary>
    /// Whether a result is within the search's ranges of duration and publish date. A result whose duration
    /// or date isn't known is outside any range on it. The other terms are for the provider to apply.
    /// </summary>
    public bool Matches(MediaSearchResult result)
    {
        foreach (var term in Terms)
        {
            if (!IsRange(term.Label))
                continue;

            var range = SearchRange.Parse(term.Value);
            var isDuration = term.Label == DurationLabel;
            var value = isDuration ? result.Duration?.TotalSeconds : result.Published?.UtcTicks;
            var lower = Measure(term.Label, range.Lower);
            var upper = Measure(term.Label, range.Upper);

            // A duration must be longer than the lower end; a date may be on it, since "after 2024" takes in 2024.
            var isAboveLower = lower is null || (isDuration ? value > lower : value >= lower);
            var isBelowUpper = upper is null || value < upper;
            if (value is null || !isAboveLower || !isBelowUpper)
                return false;
        }
        return true;
    }

    // One end of a range as a number, so the ends can be compared: seconds for a length, ticks for a date.
    private static double? Measure(string rangeLabel, string end) => rangeLabel == DurationLabel
        ? ReadLength(end)?.TotalSeconds
        : ReadDate(end)?.UtcTicks;

    private static int ReadPart(Match parts, string name) =>
        parts.Groups[name].Success ? int.Parse(parts.Groups[name].Value, CultureInfo.InvariantCulture) : 0;

    [GeneratedRegex(@"^(?:(?<hours>\d+)h)?(?:(?<minutes>\d+)m)?(?:(?<seconds>\d+)s)?$", RegexOptions.IgnoreCase)]
    private static partial Regex Length();

    /// <summary>
    /// How many terms with a label one search can have. A search has one source, one author and one range
    /// of each kind.
    /// </summary>
    public static int MaxCount(string label) => label switch
    {
        ExactLabel => 3,
        ExcludeLabel => 5,
        _ => 1
    };

    /// <summary>Splits text into its terms and the words left over.</summary>
    /// <param name="isKnown">
    /// Decides whether something written like a term really is one. Anything it turns down stays in the
    /// text as ordinary words, so a link such as <c>https://example.com</c> or a mistyped label is still searched for.
    /// </param>
    public static SearchQuery Parse(string text, Func<SearchTerm, bool> isKnown)
    {
        var terms = new List<SearchTerm>();
        var words = new List<string>();
        foreach (Match part in TermOrWord().Matches(text))
        {
            var term = ReadTerm(part);
            if (term is not null && isKnown(term))
                terms.Add(term);
            else
                words.Add(part.Value);
        }
        return new SearchQuery(string.Join(' ', words), terms);
    }

    public override string ToString()
    {
        var parts = Terms.Where(term => !string.IsNullOrWhiteSpace(term.Value)).Select(term => term.ToString()).ToList();
        if (Text.Length > 0)
            parts.Add(Text);

        return string.Join(' ', parts);
    }

    /// <summary>
    /// The search as the words a provider is sent, with the terms written the way search engines read them:
    /// an exact phrase in quotation marks, an excluded one after a minus sign. An author has no such form,
    /// so the name is sent as an exact phrase, which favours that author without ruling out others.
    /// </summary>
    public string ToSearchText()
    {
        var parts = new List<string>();
        if (Text.Length > 0)
            parts.Add(Text);

        foreach (var term in Terms)
        {
            // A term that is still being typed has no value yet.
            if (string.IsNullOrWhiteSpace(term.Value))
                continue;

            var quoted = $"\"{term.Value}\"";
            if (term.Label is ExactLabel or AuthorLabel)
                parts.Add(quoted);
            else if (term.Label == ExcludeLabel)
                parts.Add("-" + quoted);
        }
        return string.Join(' ', parts);
    }

    // A quoted term such as exact:"lofi beats" is one part even though it has a space in it; anything else is
    // split at spaces.
    [GeneratedRegex("""(?<label>[A-Za-z]+):"(?<quoted>[^"]+)"|\S+""")]
    private static partial Regex TermOrWord();

    // Reads label:value or label:"value", where the label is letters only.
    private static SearchTerm? ReadTerm(Match part)
    {
        if (part.Groups["quoted"].Success)
            return new SearchTerm(part.Groups["label"].Value.ToLowerInvariant(), part.Groups["quoted"].Value);

        var word = part.Value;
        var separator = word.IndexOf(':');
        var hasLabelAndValue = separator > 0 && separator < word.Length - 1;
        if (!hasLabelAndValue)
            return null;

        var label = word[..separator];
        var value = word[(separator + 1)..];
        return label.All(char.IsAsciiLetter) ? new SearchTerm(label.ToLowerInvariant(), value) : null;
    }
}
