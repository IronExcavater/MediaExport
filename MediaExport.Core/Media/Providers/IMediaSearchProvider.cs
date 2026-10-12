// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;

namespace MediaExport.Core.Media.Providers;

/// <summary>How a source ranks what it finds.</summary>
public enum SearchSort
{
    /// <summary>The source's own idea of what fits the search best.</summary>
    Relevance,

    /// <summary>The most viewed first.</summary>
    Views
}

/// <summary>Searches a single media source such as YouTube.</summary>
public interface IMediaSearchProvider
{
    /// <summary>A stable identifier, e.g. <c>youtube</c>, used when saving and comparing.</summary>
    string Id { get; }

    /// <summary>The name shown to the user, e.g. <c>YouTube</c>.</summary>
    string DisplayName { get; }

    /// <summary>The resource key of its icon in the app's Resources/Icons.xaml, e.g. <c>YouTubeIcon</c>.</summary>
    string Icon { get; }

    /// <summary>Searches the source. A failure is thrown, not returned as an empty list.</summary>
    /// <param name="skip">How many results to pass over first. Page 3 of 20 a page skips 40.</param>
    /// <param name="limit">The most results wanted. The source may have fewer.</param>
    Task<IReadOnlyList<MediaSearchResult>> SearchAsync(string query, int skip = 0, int limit = 10, SearchSort sort = SearchSort.Relevance, CancellationToken cancellationToken = default);

    /// <summary>Reads the media a link is to. A failure is thrown.</summary>
    /// <returns>The media, or <c>null</c> if the link isn't to this source or isn't to one item.</returns>
    Task<MediaSearchResult?> FindByLinkAsync(Uri link, CancellationToken cancellationToken = default);
}
