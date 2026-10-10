// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using MediaExport.Core.Storage;
using System.Text.Json.Serialization;

namespace MediaExport.Core.Media.Providers;

/// <summary>One search and what it found.</summary>
public sealed record CachedSearch(string ProviderId, string Query, DateTimeOffset SearchedAt, IReadOnlyList<MediaSearchResult> Results);

/// <summary>
/// Remembers what recent searches found, so repeating one shows its results at once instead of asking the
/// provider again. The searches are kept in a file, so they are still there after the app is restarted.
/// </summary>
/// <param name="path">The cache file, or <c>null</c> for a cache that lasts only as long as the app runs.</param>
public sealed class SearchCache(string? path)
{
    private const int Capacity = 50;

    // Results older than this are searched for again, because view counts and what ranks first move on.
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    // The oldest search is first.
    private readonly List<CachedSearch> _searches = JsonFile.Load(path, SearchCacheJson.Default.ListCachedSearch) ?? [];

    /// <returns>What the search found last time, or <c>null</c> if it isn't remembered or is too old.</returns>
    public IReadOnlyList<MediaSearchResult>? Find(string providerId, string query)
    {
        var search = _searches.Find(candidate => IsSame(candidate, providerId, query));
        if (search is null)
            return null;

        var age = DateTimeOffset.UtcNow - search.SearchedAt;
        return age < MaxAge ? search.Results : null;
    }

    public void Store(string providerId, string query, IReadOnlyList<MediaSearchResult> results)
    {
        _searches.RemoveAll(candidate => IsSame(candidate, providerId, query));
        _searches.Add(new CachedSearch(providerId, query, DateTimeOffset.UtcNow, results));

        var excess = _searches.Count - Capacity;
        if (excess > 0)
            _searches.RemoveRange(0, excess);

        // The whole file is rewritten for each search stored, which is fine at this capacity.
        JsonFile.Save(path, _searches, SearchCacheJson.Default.ListCachedSearch);
    }

    private static bool IsSame(CachedSearch search, string providerId, string query) =>
        search.ProviderId == providerId && string.Equals(search.Query, query, StringComparison.OrdinalIgnoreCase);
}

[JsonSerializable(typeof(List<CachedSearch>))]
internal sealed partial class SearchCacheJson : JsonSerializerContext;
