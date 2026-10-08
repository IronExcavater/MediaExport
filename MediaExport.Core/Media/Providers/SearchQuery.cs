// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MediaExport.Core.Media.Providers;

/// <summary>
/// What someone typed to search for, read into its terms:
/// <list type="bullet">
/// <item>plain words, which are what the sources are asked for;</item>
/// <item><c>"an exact phrase"</c> results must say, and <c>-word</c> they mustn't;</item>
/// <item>labelled terms that narrow what comes back: <c>creator:name</c>, <c>kind:audio</c>, <c>longer:10m</c>,
/// <c>shorter:1h</c>, <c>before:2020</c>, <c>after:2019-06</c> (a value with spaces goes in quotes);</item>
/// <item><c>source:name</c>, which names where to look rather than what to keep (see <see cref="TakeSources"/>).</item>
/// </list>
/// Sources each have their own rules for such things, or none, so the app applies them itself to whatever
/// comes back and they work the same everywhere. A new labelled term is one more line in <see cref="Terms"/>.
/// </summary>
public sealed partial class SearchQuery
{
    /// <summary>A labelled term: how its value is read (<c>null</c> if it makes no sense) and which results it then allows.</summary>
    private sealed record Term(string Label, Func<string, object?> Read, Func<object, MediaSearchResult, bool> Allows);

    private static readonly Term[] Terms =
    [
        new("creator", value => value, (value, result) => Says(result.Creator, (string)value)),
        new("kind", ReadKind, (value, result) => result.Kind == (MediaKind)value),
        new("longer", ReadLength, (value, result) => result.Duration > (TimeSpan)value),
        new("shorter", ReadLength, (value, result) => result.Duration < (TimeSpan)value),
        new("before", ReadDate, (value, result) => result.Published < (DateTimeOffset)value),
        new("after", ReadDate, (value, result) => result.Published >= (DateTimeOffset)value)
    ];

    private readonly List<string> _words = [], _phrases = [], _unwanted = [];
    private readonly List<(Term Term, object Value)> _filters = [];

    private SearchQuery(string text)
    {
        foreach (Match token in Token().Matches(text))
        {
            string label = token.Groups["label"].Value.ToLowerInvariant();
            bool isQuoted = token.Groups["quoted"].Success;
            string value = isQuoted ? token.Groups["quoted"].Value.Trim() : token.Groups["bare"].Value;
            if (value.Length == 0)
                continue; // A label still waiting for its value, or an empty pair of quotes.

            if (Terms.FirstOrDefault(term => term.Label == label) is { } term && term.Read(value) is { } read)
                _filters.Add((term, read));
            else if (label.Length > 0)
                _words.Add(token.Value); // Not a term the app knows ("re:zero", a source nobody has), so just words.
            else if (token.Groups["minus"].Success)
                _unwanted.Add(value);
            else if (isQuoted)
                _phrases.Add(value);
            else
                _words.Add(value);
        }
    }

    /// <summary>
    /// What to ask the sources for: the words and phrases, or with neither, the creator being looked for.
    /// Empty when there's nothing a source could be asked.
    /// </summary>
    public string Words => _words.Concat(_phrases).ToArray() is { Length: > 0 } said
        ? string.Join(' ', said)
        : _filters.Where(filter => filter.Term.Label == "creator").Select(filter => (string)filter.Value).FirstOrDefault() ?? string.Empty;

    /// <summary>True when there's something beyond plain words to hold results to.</summary>
    public bool IsNarrowed => _phrases.Count + _unwanted.Count + _filters.Count > 0;

    public static SearchQuery Parse(string text) => new(text);

    /// <summary>
    /// Whether a result passes every term. One that doesn't say what a term asks about (a length, a date)
    /// can't be shown to pass it, so it's left out.
    /// </summary>
    public bool Allows(MediaSearchResult result)
    {
        string said = $"{result.Title} {result.Creator}";
        return _phrases.All(phrase => Says(said, phrase))
            && !_unwanted.Any(word => Says(said, word))
            && _filters.All(filter => filter.Term.Allows(filter.Value, result));
    }

    public IReadOnlyList<MediaSearchResult> Keep(IReadOnlyList<MediaSearchResult> results) =>
        IsNarrowed ? results.Where(Allows).ToArray() : results;

    /// <summary>
    /// Takes the <c>source:</c> terms out of some text, giving the text without them and the sources they named.
    /// Only names <paramref name="isSource"/> recognises are taken; anything else is left to be read as words.
    /// </summary>
    /// <param name="includeUnfinished">
    /// Whether to take a term the text ends with. While someone's typing it may be only half a name
    /// (<c>source:you</c>); once they paste or press Enter, it's all there is.
    /// </param>
    public static (string Text, IReadOnlyList<string> Sources) TakeSources(string text, bool includeUnfinished, Func<string, bool> isSource)
    {
        var sources = new List<string>();
        string rest = SourceTerm().Replace(text, term =>
        {
            string name = term.Groups["quoted"].Success ? term.Groups["quoted"].Value : term.Groups["bare"].Value;
            bool isFinished = term.Groups["after"].Length > 0 || includeUnfinished;
            if (!isFinished || !isSource(name))
                return term.Value;

            sources.Add(name);
            return string.Empty;
        });
        return (sources.Count > 0 ? rest : text, sources);
    }

    private static bool Says(string? text, string words) =>
        text?.Contains(words, StringComparison.CurrentCultureIgnoreCase) == true;

    private static object? ReadKind(string value) => value.ToLowerInvariant() switch
    {
        "video" => MediaKind.Video,
        "audio" => MediaKind.Audio,
        _ => null
    };

    // 90s, 10m, 1h30m; a bare number is minutes; or as a clock reads, 1:30:00.
    private static object? ReadLength(string value)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double minutes))
            return TimeSpan.FromMinutes(minutes);

        Match units = LengthInUnits().Match(value);
        if (units.Success && units.Length > 0)
            return new TimeSpan(Number(units.Groups[1]), Number(units.Groups[2]), Number(units.Groups[3]));

        string[] clock = value.Split(':');
        return clock.Length is 2 or 3 && clock.All(part => int.TryParse(part, out _))
            ? clock.Aggregate(TimeSpan.Zero, (total, part) => total * 60 + TimeSpan.FromSeconds(int.Parse(part, CultureInfo.InvariantCulture)))
            : null;

        static int Number(Group digits) => digits.Success ? int.Parse(digits.Value, CultureInfo.InvariantCulture) : 0;
    }

    // A year, a month or a day, meaning the moment it begins: 2020, 2020-06, 2020-06-17.
    private static object? ReadDate(string value) =>
        DateTimeOffset.TryParseExact(value, ["yyyy", "yyyy-MM", "yyyy-M", "yyyy-MM-dd", "yyyy-M-d"], CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTimeOffset date)
            ? date
            : null;

    // A label still waiting for its value; or -word, "a phrase", label:value, label:"a value".
    // An opening quote without its partner runs to the end.
    [GeneratedRegex("""(?<label>[A-Za-z]+):(?=\s|$)|(?<minus>-)?(?:(?<label>[A-Za-z]+):)?(?:"(?<quoted>[^"]*)"?|(?<bare>[^\s"]+))""")]
    private static partial Regex Token();

    [GeneratedRegex("""(?<=^|\s)source:(?:"(?<quoted>[^"]*)"|(?<bare>[^\s"]+))(?<after>\s*)(?=\S|$)""", RegexOptions.IgnoreCase)]
    private static partial Regex SourceTerm();

    [GeneratedRegex(@"^(?:(\d+)h)?(?:(\d+)m)?(?:(\d+)s)?$", RegexOptions.IgnoreCase)]
    private static partial Regex LengthInUnits();
}
