// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;

namespace MediaExport.Core.Media.Providers;

/// <summary>What decides which results a search brings back, and so which come first.</summary>
public enum SearchSort
{
    Relevance,
    Popularity
}

/// <summary>A remote slice of search results, whether it can advance, and whether every requested source answered.</summary>
public sealed record SearchResultPage(IReadOnlyList<MediaSearchResult> Results, bool HasMore, bool IsComplete = true);

public interface IMediaSearchProvider
{
    string Id { get; }
    string DisplayName { get; }

    /// <summary>Its icon's name in the app's icons, e.g. <c>internet-archive</c>; not tied to <see cref="Id"/>.</summary>
    string Icon { get; }

    /// <summary>Whether a later offset is guaranteed to fetch a distinct remote slice.</summary>
    bool SupportsPaging => false;

    /// <summary>Whether the provider exposes a real popular or trending feed without an invented query.</summary>
    bool SupportsPopular => false;

    /// <param name="sort">Asked of the source where it can order its own results; sources that can't answer by relevance.</param>
    /// <param name="limit">The most results needed. Compact suggestions use ten; the Search page asks for enough to page.</param>
    Task<IReadOnlyList<MediaSearchResult>> SearchAsync(
        string query,
        SearchSort sort = SearchSort.Relevance,
        CancellationToken cancellationToken = default,
        int limit = 10);

    /// <summary>Searches a real remote range rather than slicing a previously returned list.</summary>
    async Task<SearchResultPage> SearchAsync(
        string query,
        SearchSort sort,
        int offset,
        int take,
        CancellationToken cancellationToken = default)
    {
        offset = Math.Max(0, offset);
        take = Math.Max(1, take);
        IReadOnlyList<MediaSearchResult> found = await SearchAsync(query, sort, cancellationToken, checked(offset + take + 1));
        return new SearchResultPage(found.Skip(offset).Take(take).ToArray(), found.Count > offset + take);
    }

    /// <summary>The source's own popular or trending feed, without an invented search query.</summary>
    Task<SearchResultPage> PopularAsync(int offset, int take, CancellationToken cancellationToken = default) =>
        Task.FromResult(new SearchResultPage([], false));
}
