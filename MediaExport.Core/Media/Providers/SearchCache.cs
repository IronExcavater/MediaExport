// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using MediaExport.Core.Storage;
using System.Text.Json.Serialization;

namespace MediaExport.Core.Media.Providers;

/// <summary>One page of a search and what it found.</summary>
public sealed record CachedSearch(string ProviderId, string Query, int Page, DateTimeOffset SearchedAt, IReadOnlyList<MediaSearchResult> Results);

/// <summary>
/// Remembers what recent searches found, page by page, so repeating one shows its results at once instead of asking the
/// provider again. It keeps the first page of the last 50 searches and the last 30 later pages. The searches are kept in a file, so they are still there after the app is restarted.
/// </summary>
/// <param name="path">The cache file, or <c>null</c> for a cache that lasts only as long as the app runs.</param>
public sealed class SearchCache(string? path)
{
    // First pages are limited separately from later ones, so paging deep into one search can't push out
    // the first pages of other searches, which are the ones that get repeated.
    private const int FirstPageCapacity = 50;
    private const int LaterPageCapacity = 30;

    // Results older than this are searched for again, because view counts and what ranks first move on.
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    // The oldest search is first.
    private readonly List<CachedSearch> _searches = JsonFile.Load(path, SearchCacheJson.Default.ListCachedSearch) ?? [];

    /// <returns>What the search found last time, or <c>null</c> if it isn't remembered or is too old.</returns>
    public IReadOnlyList<MediaSearchResult>? Find(string providerId, string query, int page)
    {
        var search = _searches.Find(candidate => IsSame(candidate, providerId, query, page));
        if (search is null)
            return null;

        var age = DateTimeOffset.UtcNow - search.SearchedAt;
        return age < MaxAge ? search.Results : null;
    }

    public void Store(string providerId, string query, int page, IReadOnlyList<MediaSearchResult> results)
    {
        _searches.RemoveAll(candidate => IsSame(candidate, providerId, query, page));
        _searches.Add(new CachedSearch(providerId, query, page, DateTimeOffset.UtcNow, results));

        RemoveOldest(search => search.Page == 1, FirstPageCapacity);
        RemoveOldest(search => search.Page > 1, LaterPageCapacity);

        // The whole file is rewritten for each search stored, which is fine at this capacity.
        JsonFile.Save(path, _searches, SearchCacheJson.Default.ListCachedSearch);
    }

    private void RemoveOldest(Func<CachedSearch, bool> isCounted, int capacity)
    {
        var excess = _searches.Count(isCounted) - capacity;
        for (var removed = 0; removed < excess; removed++)
        {
            var oldest = _searches.First(isCounted);
            _searches.Remove(oldest);
        }
    }

    private static bool IsSame(CachedSearch search, string providerId, string query, int page) =>
        search.ProviderId == providerId && search.Page == page && string.Equals(search.Query, query, StringComparison.OrdinalIgnoreCase);
}

[JsonSerializable(typeof(List<CachedSearch>))]
internal sealed partial class SearchCacheJson : JsonSerializerContext;
