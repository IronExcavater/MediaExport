// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MediaExport.Core.Media.Providers;

/// <summary>One term of a search, written <c>label:value</c> or <c>label:value1,value2</c>.</summary>
public sealed record SearchTerm(string Label, string Value)
{
    private const char ValueSeparator = ',';

    public SearchTerm(string label, IEnumerable<string> values)
        : this(label, string.Join(ValueSeparator, values))
    {
    }

    /// <summary>The values of a term that may have several, split at its commas.</summary>
    public IReadOnlyList<string> Values =>
        Value.Split(ValueSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public override string ToString()
    {
        var hasSpace = Value.Any(char.IsWhiteSpace);
        return hasSpace ? $"{Label}:\"{Value}\"" : $"{Label}:{Value}";
    }
}

/// <summary>A range term's two ends, written <c>3m..10m</c>. An end left out, as in <c>3m..</c>, is empty.</summary>
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

/// <summary>A search as text, terms then words, such as <c>source:youtube exact:"lofi beats" study</c>.</summary>
/// <param name="Text">The words to search for, without any terms.</param>
public sealed partial record SearchQuery(string Text, IReadOnlyList<SearchTerm> Terms)
{
    /// <summary>The label of the term that names the providers to search.</summary>
    public const string SourceLabel = "source";

    /// <summary>The label of a term for a word or phrase that results must contain as it is written.</summary>
    public const string ExactLabel = "exact";

    /// <summary>The label of a term for a word or phrase that results must not contain.</summary>
    public const string ExcludeLabel = "exclude";

    /// <summary>The label of a term for who made the media.</summary>
    public const string AuthorLabel = "author";

    /// <summary>Another way to write <see cref="AuthorLabel"/>, which is read as it.</summary>
    public const string CreatorLabel = "creator";

    /// <summary>The label of the term for a range of durations, written as <c>duration:3m..10m</c>.</summary>
    public const string DurationLabel = "duration";

    /// <summary>The label of the term for a range of publish dates, as in <c>published:2024..2025-06</c>.</summary>
    public const string PublishedLabel = "published";

    /// <summary>Shorter ways to write one end of a range, so <c>longer:3m</c> is <c>duration:3m..</c>.</summary>
    public const string LongerLabel = "longer";
    public const string ShorterLabel = "shorter";
    public const string AfterLabel = "after";
    public const string BeforeLabel = "before";

    /// <summary>The label of the term for how the results are ranked, written as <c>sort:views</c>.</summary>
    public const string SortLabel = "sort";

    public static SearchTerm Source(IEnumerable<string> providerIds) => new(SourceLabel, providerIds);

    /// <summary>Whether a label is for a term whose value is a <see cref="SearchRange"/>.</summary>
    public static bool IsRange(string label) => label is DurationLabel or PublishedLabel;

    /// <summary>The label of the range that a label for one end belongs to, or <c>null</c> for any other.</summary>
    public static string? RangeLabelOf(string endLabel) => endLabel switch
    {
        LongerLabel or ShorterLabel => DurationLabel,
        AfterLabel or BeforeLabel => PublishedLabel,
        _ => null
    };

    /// <summary>Whether a term's value can be read. A range needs a readable end and room between its ends.</summary>
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

    /// <summary>Sets one end of a range from a term like <c>longer:3m</c>. The other end goes if they cross.</summary>
    /// <param name="range">The range term the search already has, or <c>null</c> if it has none.</param>
    public static SearchTerm WithEnd(SearchTerm? range, SearchTerm end)
    {
        var label = RangeLabelOf(end.Label)
            ?? throw new ArgumentException($"'{end.Label}' isn't an end of a range.", nameof(end));
        var ends = range is null ? new SearchRange(string.Empty, string.Empty) : SearchRange.Parse(range.Value);
        var isLower = end.Label is LongerLabel or AfterLabel;
        var changed = isLower ? ends with { Lower = end.Value } : ends with { Upper = end.Value };

        var term = new SearchTerm(label, changed.ToString());
        if (IsValid(term))
            return term;

        var alone = isLower ? new SearchRange(end.Value, string.Empty) : new SearchRange(string.Empty, end.Value);
        return new SearchTerm(label, alone.ToString());
    }

    /// <summary>Reads a length such as <c>90s</c>, <c>1h30m</c>, <c>1:30</c>, or a number of minutes.</summary>
    public static TimeSpan? ReadLength(string value)
    {
        var text = value.Replace(" ", string.Empty);
        if (int.TryParse(text, out var minutes))
            return TimeSpan.FromMinutes(minutes);

        var clock = Clock().Match(text);
        if (clock.Success)
            return new TimeSpan(ReadPart(clock, "hours"), ReadPart(clock, "minutes"), 0);

        var parts = Length().Match(text);
        if (!parts.Success || text.Length == 0)
            return null;

        return new TimeSpan(ReadPart(parts, "hours"), ReadPart(parts, "minutes"), ReadPart(parts, "seconds"));
    }

    /// <summary>Reads a date such as <c>2024</c>, <c>2024-06-01</c>, or a day in regional format.</summary>
    public static DateTimeOffset? ReadDate(string value)
    {
        var text = value.Trim();
        var isStandard = DateTimeOffset.TryParseExact(
            text,
            StandardDateFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal,
            out var date);
        if (isStandard)
            return date;

        // A bare number would be read as a day of this month, which nobody means by it.
        var hasOnlyDigits = text.All(char.IsAsciiDigit);
        var isRegional = !hasOnlyDigits
            && DateTimeOffset.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out date);
        return isRegional ? date : null;
    }

    /// <summary>Writes one end of a range the standard way, so <c>1:30</c> is <c>1h30m</c>. Others are kept.</summary>
    public static string NormalizeEnd(string rangeLabel, string typed)
    {
        var text = typed.Trim();
        if (rangeLabel == DurationLabel)
        {
            var length = ReadLength(text);
            return length is null ? text : WriteLength(length.Value);
        }

        // A year or a month written the standard way stays as it is; any other date is written as its day.
        var isStandard = DateTimeOffset.TryParseExact(
            text,
            StandardDateFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal,
            out _);
        var date = ReadDate(text);
        return isStandard || date is null ? text : date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static readonly string[] StandardDateFormats = ["yyyy", "yyyy-MM", "yyyy-MM-dd"];

    // A length with a unit after each part that isn't zero: 45m, 2h, 1h30m, 90s as 1m30s.
    private static string WriteLength(TimeSpan length)
    {
        var hours = (int)length.TotalHours;
        var written = string.Empty;
        if (hours > 0)
            written += $"{hours}h";
        if (length.Minutes > 0)
            written += $"{length.Minutes}m";
        if (length.Seconds > 0)
            written += $"{length.Seconds}s";

        return written;
    }

    /// <summary>Whether a result is within the duration and date ranges. One that lacks the value is outside.</summary>
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

    [GeneratedRegex(@"^(?<hours>\d+):(?<minutes>\d{1,2})$")]
    private static partial Regex Clock();

    /// <summary>The link that text is, or <c>null</c> if it is anything else, such as words to search for.</summary>
    public static Uri? ReadLink(string text)
    {
        var isLink = Uri.TryCreate(text.Trim(), UriKind.Absolute, out var link);
        var isWebLink = isLink && link!.Scheme is "http" or "https";
        var isOneWord = !text.Trim().Any(char.IsWhiteSpace);
        return isWebLink && isOneWord ? link : null;
    }

    /// <summary>How many terms with a label one search can have.</summary>
    public static int MaxCount(string label) => label switch
    {
        ExactLabel => 3,
        ExcludeLabel => 5,
        _ => 1
    };

    /// <summary>Splits text into its terms and the words left over.</summary>
    /// <param name="isKnown">Whether something written like a term is one. Anything else stays as words.</param>
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
        var termsWithValues = Terms.Where(term => !string.IsNullOrWhiteSpace(term.Value));
        var parts = termsWithValues.Select(term => term.ToString()).ToList();
        if (Text.Length > 0)
            parts.Add(Text);

        return string.Join(' ', parts);
    }

    /// <summary>The words sent to a provider, with exact and author terms quoted and excluded ones negated.</summary>
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

    // A quoted term such as exact:"lofi beats" is one part despite its space; anything else is split at spaces.
    [GeneratedRegex("""(?<label>[A-Za-z]+):"(?<quoted>[^"]+)"|\S+""")]
    private static partial Regex TermOrWord();

    // Reads label:value or label:"value", where the label is letters only.
    private static SearchTerm? ReadTerm(Match part)
    {
        if (part.Groups["quoted"].Success)
            return new SearchTerm(ReadLabel(part.Groups["label"].Value), part.Groups["quoted"].Value);

        var word = part.Value;
        var separator = word.IndexOf(':');
        var hasLabelAndValue = separator > 0 && separator < word.Length - 1;
        if (!hasLabelAndValue)
            return null;

        var label = word[..separator];
        var value = word[(separator + 1)..];
        return label.All(char.IsAsciiLetter) ? new SearchTerm(ReadLabel(label), value) : null;
    }

    private static string ReadLabel(string written)
    {
        var label = written.ToLowerInvariant();
        return label == CreatorLabel ? AuthorLabel : label;
    }
}
