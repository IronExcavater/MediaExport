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

/// <summary>What a link is to on a source.</summary>
public enum LinkKind
{
    /// <summary>The link isn't to this source.</summary>
    None,

    /// <summary>One video or track.</summary>
    Item,

    /// <summary>Several items, such as a playlist.</summary>
    List,

    /// <summary>A creator's page, such as a channel.</summary>
    Creator
}

/// <summary>Searches a single media source such as YouTube.</summary>
public interface IMediaSearchProvider
{
    /// <summary>A stable identifier, e.g. <c>youtube</c>, used when saving and comparing.</summary>
    string Id { get; }

    /// <summary>The name shown to the user, e.g. <c>YouTube</c>.</summary>
    string DisplayName { get; }

    /// <summary>Whether what it finds is video or audio.</summary>
    MediaKind Kind { get; }

    /// <summary>The resource key of its icon in the app's Resources/Icons.xaml, e.g. <c>YouTubeIcon</c>.</summary>
    string Icon { get; }

    /// <summary>
    /// Searches the source. An empty query asks for what is popular there, which is nothing on a source that
    /// doesn't say. A failure is thrown, not returned as an empty list.
    /// </summary>
    /// <param name="skip">How many results to pass over first. Page 3 of 20 a page skips 40.</param>
    /// <param name="limit">The most results wanted. The source may have fewer.</param>
    Task<IReadOnlyList<MediaSearchResult>> SearchAsync(
        string query,
        int skip = 0,
        int limit = 10,
        SearchSort sort = SearchSort.Relevance,
        CancellationToken cancellationToken = default);

    /// <summary>What a link is to on this source, told from its address alone.</summary>
    LinkKind GetLinkKind(Uri link);

    /// <summary>Reads the media a link is to. A failure is thrown.</summary>
    /// <returns>The media, or <c>null</c> if the link isn't to one item on this source.</returns>
    Task<MediaSearchResult?> FindByLinkAsync(Uri link, CancellationToken cancellationToken = default);

    /// <summary>Reads what a link to a list holds, such as the videos of a playlist. A failure is thrown.</summary>
    Task<IReadOnlyList<MediaSearchResult>> ListByLinkAsync(
        Uri link,
        int skip = 0,
        int limit = 10,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the name of the creator whose page a link is to. A failure is thrown.</summary>
    /// <returns>The name, or <c>null</c> if the source doesn't say.</returns>
    Task<string?> FindCreatorAsync(Uri link, CancellationToken cancellationToken = default);
}
