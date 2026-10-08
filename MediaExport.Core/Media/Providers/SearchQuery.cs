// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MediaExport.Core.Media.Providers;

/// <summary>How a structured search term's value is edited.</summary>
public enum SearchTermKind
{
    Text,
    Length,
    Date
}

/// <summary>The structured parts of a search that can be edited independently of its ordinary words.</summary>
public enum SearchTermType
{
    ExactPhrase,
    ExcludedWord,
    Creator,
    LongerThan,
    ShorterThan,
    Before,
    After
}

public sealed record SearchTermDefinition(SearchTermType Type, string Label, SearchTermKind Kind, bool Repeatable);
public sealed record ParsedSearchTerm(SearchTermType Type, string Value);
public sealed record SearchExtraction(string Text, IReadOnlyList<ParsedSearchTerm> Terms, IReadOnlyList<string> Sources);

/// <summary>
/// What someone typed to search for. Ordinary words are sent to sources; exact phrases, excluded words,
/// creators, durations and dates consistently narrow the results after they return.
/// </summary>
public sealed partial class SearchQuery
{
    public const string SourceLabel = "source";

    private sealed record Term(
        SearchTermDefinition Definition,
        Func<string, object?> Read,
        Func<object, MediaSearchResult, bool> Allows);

    private static readonly SearchTermDefinition ExactPhrase = new(SearchTermType.ExactPhrase, "phrase", SearchTermKind.Text, true);
    private static readonly SearchTermDefinition ExcludedWord = new(SearchTermType.ExcludedWord, "exclude", SearchTermKind.Text, true);

    private static readonly Term[] FilterTerms =
    [
        new(new(SearchTermType.Creator, "creator", SearchTermKind.Text, false), value => value, (value, result) => Says(result.Creator, (string)value)),
        new(new(SearchTermType.LongerThan, "longer", SearchTermKind.Length, false), ReadLength, (value, result) => result.Duration > (TimeSpan)value),
        new(new(SearchTermType.ShorterThan, "shorter", SearchTermKind.Length, false), ReadLength, (value, result) => result.Duration < (TimeSpan)value),
        new(new(SearchTermType.Before, "before", SearchTermKind.Date, false), ReadDate, (value, result) => result.Published < (DateTimeOffset)value),
        new(new(SearchTermType.After, "after", SearchTermKind.Date, false), ReadDate, (value, result) => result.Published >= (DateTimeOffset)value)
    ];

    public static IReadOnlyList<SearchTermDefinition> Definitions { get; } =
        [ExactPhrase, ExcludedWord, .. FilterTerms.Select(term => term.Definition)];

    private readonly List<string> _words = [], _phrases = [], _unwanted = [];
    private readonly List<(Term Term, object Value)> _filters = [];

    private SearchQuery(string text)
    {
        foreach (Match token in Token().Matches(text))
        {
            string label = token.Groups["label"].Value.ToLowerInvariant();
            bool isQuoted = token.Groups["quoted"].Success;
            string value = (isQuoted ? UnescapeQuoted(token.Groups["quoted"].Value) : token.Groups["bare"].Value).Trim();
            if (value.Length == 0)
                continue;

            if (FilterTerms.FirstOrDefault(term => term.Definition.Label == label) is { } term && term.Read(value) is { } read)
                _filters.Add((term, read));
            else if (label.Length > 0)
                _words.Add(token.Value);
            else if (token.Groups["minus"].Success)
                _unwanted.Add(value);
            else if (isQuoted)
                _phrases.Add(value);
            else
                _words.Add(value);
        }
    }

    /// <summary>
    /// What to ask the sources for: ordinary words and phrases, or with neither, the creator being sought.
    /// Empty when there is nothing a source could be asked.
    /// </summary>
    public string Words => _words.Concat(_phrases).ToArray() is { Length: > 0 } said
        ? string.Join(' ', said)
        : _filters.Where(filter => filter.Term.Definition.Type == SearchTermType.Creator)
            .Select(filter => (string)filter.Value)
            .FirstOrDefault() ?? string.Empty;

    public bool IsNarrowed => _phrases.Count + _unwanted.Count + _filters.Count > 0;

    public static SearchQuery Parse(string text) => new(text);

    public bool Allows(MediaSearchResult result)
    {
        string said = $"{result.Title} {result.Creator}";
        return _phrases.All(phrase => Says(said, phrase))
            && !_unwanted.Any(word => Says(said, word))
            && _filters.All(filter => filter.Term.Allows(filter.Value, result));
    }

    public IReadOnlyList<MediaSearchResult> Keep(IReadOnlyList<MediaSearchResult> results) =>
        IsNarrowed ? results.Where(Allows).ToArray() : results;

    public static string Write(SearchTermType type, string value) => type switch
    {
        SearchTermType.ExactPhrase => Quote(value),
        SearchTermType.ExcludedWord => $"-{QuoteIfNeeded(value)}",
        _ => $"{Definition(type).Label}:{QuoteIfNeeded(value)}"
    };

    public static string WriteSource(string value) => $"{SourceLabel}:{QuoteIfNeeded(value)}";

    /// <summary>
    /// Removes complete, valid editable terms and recognized sources from text. Invalid or unfinished syntax
    /// remains ordinary text so resolving a query can never make part of it disappear.
    /// </summary>
    public static SearchExtraction ExtractTerms(string text, bool includeUnfinished, Func<string, bool> isSource)
    {
        var remaining = new StringBuilder();
        var terms = new List<ParsedSearchTerm>();
        var sources = new List<string>();
        int cursor = 0;

        foreach (Match token in Token().Matches(text))
        {
            remaining.Append(text, cursor, token.Index - cursor);
            string label = token.Groups["label"].Value.ToLowerInvariant();
            bool isQuoted = token.Groups["quoted"].Success;
            string value = (isQuoted ? UnescapeQuoted(token.Groups["quoted"].Value) : token.Groups["bare"].Value).Trim();
            bool isFinished = token.Index + token.Length < text.Length || includeUnfinished;
            bool wasTaken = false;

            if (isFinished && value.Length > 0)
            {
                if (label == SourceLabel && isSource(value))
                {
                    sources.Add(value);
                    wasTaken = true;
                }
                else if (FilterTerms.FirstOrDefault(term => term.Definition.Label == label) is { } term && term.Read(value) is not null)
                {
                    terms.Add(new ParsedSearchTerm(term.Definition.Type, value));
                    wasTaken = true;
                }
                else if (label.Length == 0 && token.Groups["minus"].Success)
                {
                    terms.Add(new ParsedSearchTerm(SearchTermType.ExcludedWord, value));
                    wasTaken = true;
                }
                else if (label.Length == 0 && isQuoted && token.Groups["close"].Success)
                {
                    terms.Add(new ParsedSearchTerm(SearchTermType.ExactPhrase, value));
                    wasTaken = true;
                }
            }

            if (!wasTaken)
                remaining.Append(token.Value);
            cursor = token.Index + token.Length;
        }
        remaining.Append(text, cursor, text.Length - cursor);

        return new SearchExtraction(Whitespace().Replace(remaining.ToString(), " ").Trim(), terms, sources);
    }

    private static SearchTermDefinition Definition(SearchTermType type) =>
        Definitions.First(definition => definition.Type == type);

    private static string QuoteIfNeeded(string value) =>
        value.Any(character => char.IsWhiteSpace(character) || character is '"' or '\\' or ':') ? Quote(value) : value;

    private static string Quote(string value) =>
        $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private static string UnescapeQuoted(string value)
    {
        var result = new StringBuilder(value.Length);
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (character == '\\' && index + 1 < value.Length && value[index + 1] is '\\' or '"')
                character = value[++index];
            result.Append(character);
        }
        return result.ToString();
    }

    private static bool Says(string? text, string words) =>
        text?.Contains(words, StringComparison.CurrentCultureIgnoreCase) == true;

    private static object? ReadLength(string value)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double minutes))
            return double.IsFinite(minutes) && minutes >= 0 && minutes <= TimeSpan.MaxValue.TotalMinutes
                ? TimeSpan.FromMinutes(minutes)
                : null;

        Match units = LengthInUnits().Match(value);
        if (units.Success && units.Length > 0)
        {
            int? hours = Number(units.Groups[1]), unitMinutes = Number(units.Groups[2]), seconds = Number(units.Groups[3]);
            if (hours is null || unitMinutes is null || seconds is null)
                return null;
            try
            {
                return new TimeSpan(hours.Value, unitMinutes.Value, seconds.Value);
            }
            catch (ArgumentException) { return null; }
            catch (OverflowException) { return null; }
        }

        string[] clock = value.Split(':');
        if (clock.Length is not (2 or 3) || !clock.All(part => int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
            return null;
        try
        {
            return clock.Aggregate(TimeSpan.Zero, (total, part) =>
                total * 60 + TimeSpan.FromSeconds(int.Parse(part, CultureInfo.InvariantCulture)));
        }
        catch (OverflowException) { return null; }

        static int? Number(Group digits) => !digits.Success ? 0
            : int.TryParse(digits.Value, NumberStyles.None, CultureInfo.InvariantCulture, out int number) ? number
            : null;
    }

    private static object? ReadDate(string value) =>
        DateTimeOffset.TryParseExact(value, ["yyyy", "yyyy-MM", "yyyy-M", "yyyy-MM-dd", "yyyy-M-d"], CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTimeOffset date)
            ? date
            : null;

    [GeneratedRegex("""(?<=^|\s)(?<label>[A-Za-z]+):(?=\s|$)|(?<=^|\s)(?<minus>-)?(?:(?<label>[A-Za-z]+):)?(?:"(?<quoted>(?:\\.|[^"])*)(?<close>")?|(?<bare>[^\s"]+))""")]
    private static partial Regex Token();

    [GeneratedRegex(@"^(?:(\d+)h)?(?:(\d+)m)?(?:(\d+)s)?$", RegexOptions.IgnoreCase)]
    private static partial Regex LengthInUnits();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
