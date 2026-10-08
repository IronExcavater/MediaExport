// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using System.Text.RegularExpressions;

namespace MediaExport.Core.Media.Providers;

/// <summary>
/// What someone typed to search for, with the two terms the app understands itself so they work the same
/// on every source: <c>"an exact phrase"</c> that results must say, and <c>-word</c> that they mustn't.
/// Sources each have their own rules for such things, or none, so they're only asked for the plain words.
/// </summary>
public sealed partial class SearchTerms
{
    private readonly string[] _phrases;
    private readonly string[] _unwanted;

    public SearchTerms(string text)
    {
        _phrases = Phrase().Matches(text).Select(match => match.Groups[1].Value.Trim()).Where(phrase => phrase.Length > 0).ToArray();
        string rest = Phrase().Replace(text, " $1 ");
        _unwanted = Unwanted().Matches(rest).Select(match => match.Groups[1].Value).ToArray();
        Query = string.Join(' ', Unwanted().Replace(rest, " ").Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>The words to ask the sources for: everything typed but the unwanted words, without the quotes.</summary>
    public string Query { get; }

    /// <summary>Whether a result's title or maker says every phrase and none of the unwanted words.</summary>
    public bool Allows(MediaSearchResult result)
    {
        string said = $"{result.Title} {result.Creator}";
        return _phrases.All(phrase => said.Contains(phrase, StringComparison.CurrentCultureIgnoreCase))
            && !_unwanted.Any(word => said.Contains(word, StringComparison.CurrentCultureIgnoreCase));
    }

    public IReadOnlyList<MediaSearchResult> Keep(IReadOnlyList<MediaSearchResult> results) =>
        _phrases.Length == 0 && _unwanted.Length == 0 ? results : results.Where(Allows).ToArray();

    [GeneratedRegex("\"([^\"]*)\"")]
    private static partial Regex Phrase();

    // A minus at the start of a word, not one inside it (so "hi-fi" stays whole).
    [GeneratedRegex(@"(?<=^|\s)-(\S+)")]
    private static partial Regex Unwanted();
}
