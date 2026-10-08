// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;

namespace MediaExport.Core.Media.Providers;

/// <summary>Puts results from one or several sources into one order.</summary>
public static class SearchRanking
{
    /// <summary>
    /// How well a result answers a search, from 0 to 1: whether its title and maker say the words searched
    /// for, how high its own source placed it (sources know things the words don't), and how much it's been
    /// played, which tells one source's best match from another's.
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
        // Each tenfold more plays counts the same again, up to a billion; unknown counts sit in the middle.
        double played = result.Views is { } views ? Math.Min(1, Math.Log10(views + 1) / 9) : 0.5;
        return 0.25 * inEither + 0.1 * inTitle + 0.1 * asPhrase + 0.3 * placed + 0.25 * played;
    }

    /// <summary>
    /// One list from each source's results. By relevance a single source keeps its own order; results
    /// without a play count or a date go last when sorting by those, in the order they came.
    /// </summary>
    /// <param name="ascending">Turns the order round (least played, oldest, loosest match first); what has no count or date still goes last.</param>
    public static IReadOnlyList<MediaSearchResult> Order(
        IReadOnlyList<IReadOnlyList<MediaSearchResult>> sources,
        string query,
        SearchSort sort,
        bool ascending = false)
    {
        var placed = sources.SelectMany(source => source.Select((result, place) => (Result: result, Place: place, Of: source.Count)));
        var ordered = sort switch
        {
            SearchSort.Popularity => placed.OrderByDescending(item => item.Result.Views ?? -1),
            SearchSort.Recency => placed.OrderByDescending(item => item.Result.Published ?? DateTimeOffset.MinValue),
            _ when sources.Count == 1 => placed.OrderBy(item => item.Place),
            _ => placed.OrderByDescending(item => Relevance(item.Result, query, item.Place, item.Of))
        };

        MediaSearchResult[] results = ordered.ThenBy(item => item.Place).Select(item => item.Result).DistinctBy(result => result.SourceUrl).ToArray();
        if (!ascending)
            return results;

        // Only what could be compared is turned round; the rest keeps its place at the end.
        int known = sort switch
        {
            SearchSort.Popularity => results.Count(result => result.Views is not null),
            SearchSort.Recency => results.Count(result => result.Published is not null),
            _ => results.Length
        };
        Array.Reverse(results, 0, known);
        return results;
    }

    private static bool Says(string? text, string words) =>
        text?.Contains(words, StringComparison.CurrentCultureIgnoreCase) == true;
}
