// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;

namespace MediaExport.Core.Media.Providers;

/// <summary>Which results come first.</summary>
public enum SearchSort
{
    Relevance,
    Popularity,
    Recency
}

public interface IMediaSearchProvider
{
    string Id { get; }
    string DisplayName { get; }

    /// <summary>Its icon's name in the app's icons, e.g. <c>internet-archive</c>; not tied to <see cref="Id"/>.</summary>
    string Icon { get; }

    /// <param name="sort">Asked of the source where it can order its own results; sources that can't answer by relevance.</param>
    Task<IReadOnlyList<MediaSearchResult>> SearchAsync(
        string query,
        SearchSort sort = SearchSort.Relevance,
        CancellationToken cancellationToken = default);
}
