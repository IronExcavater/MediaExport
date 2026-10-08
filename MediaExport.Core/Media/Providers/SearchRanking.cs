// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;

namespace MediaExport.Core.Media.Providers;

/// <summary>
/// How the results a search brought back are laid out, once <see cref="SearchSort"/> has decided which
/// results those are. <see cref="AsRanked"/> leaves them in the order that decided it.
/// </summary>
public enum SearchArrangement
{
    AsRanked,
    NewestFirst,
    OldestFirst,
    TitleAToZ,
    TitleZToA
}

/// <summary>Puts results from one or several sources into one order.</summary>
public static class SearchRanking
{
    /// <summary>
    /// How well a result answers a search, from 0 to 1: whether its title and maker say the words searched
    /// for, how high its own source placed it (sources know things the words don't), and how much it's been
    /// viewed, which helps compare one source's best match with another's.
    /// </summary>
    /// <param name="place">Where its source put it, from 0.</param>
    /// <param name="of">How many results that source gave.</param>
    public static double Relevance(MediaSearchResult result, string query, int place, int of)
    {
        string[] words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return 0;

        double inTitle = words.Count(word => Says(result.Title, word)) / (double)words.Length;
        double inEither = words.Count(word => Says(result.Title, word) || Says(result.Creator, word)) / (double)words.Length;
        double asPhrase = Says(result.Title, query.Trim()) ? 1 : 0;
        double placed = 1 - place / (double)Math.Max(1, of);
        // Each tenfold increase in views counts the same again, up to a billion; unknown counts sit in the middle.
        double viewed = result.Views is { } views ? Math.Min(1, Math.Log10(views + 1) / 9) : 0.5;
        return 0.25 * inEither + 0.1 * inTitle + 0.1 * asPhrase + 0.3 * placed + 0.25 * viewed;
    }

    /// <summary>
    /// One list from each source's results, ranked by <paramref name="sort"/> and then laid out by
    /// <paramref name="arrangement"/>. By relevance a single source keeps its own order. Results without a
    /// view count go last by popularity, and those without a date go last when arranged by date, either way round.
    /// </summary>
    public static IReadOnlyList<MediaSearchResult> Order(
        IReadOnlyList<IReadOnlyList<MediaSearchResult>> sources,
        string query,
        SearchSort sort,
        SearchArrangement arrangement = SearchArrangement.AsRanked)
    {
        var placed = sources.SelectMany(source => source.Select((result, place) => (Result: result, Place: place, Of: source.Count)));
        var ranked = sort switch
        {
            SearchSort.Popularity => placed.OrderByDescending(item => item.Result.Views ?? -1),
            _ when sources.Count == 1 => placed.OrderBy(item => item.Place),
            _ => placed.OrderByDescending(item => Relevance(item.Result, query, item.Place, item.Of))
        };
        IEnumerable<MediaSearchResult> results = ranked.ThenBy(item => item.Place).Select(item => item.Result).DistinctBy(result => result.SourceUrl);

        // Sorting keeps the ranking among results it can't tell apart, the undated ones included.
        return (arrangement switch
        {
            SearchArrangement.NewestFirst => results.OrderBy(result => result.Published is null).ThenByDescending(result => result.Published),
            SearchArrangement.OldestFirst => results.OrderBy(result => result.Published is null).ThenBy(result => result.Published),
            SearchArrangement.TitleAToZ => results.OrderBy(result => result.Title, StringComparer.CurrentCultureIgnoreCase),
            SearchArrangement.TitleZToA => results.OrderByDescending(result => result.Title, StringComparer.CurrentCultureIgnoreCase),
            _ => results
        }).ToArray();
    }

    private static bool Says(string? text, string words) =>
        text?.Contains(words, StringComparison.CurrentCultureIgnoreCase) == true;
}
