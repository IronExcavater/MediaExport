// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;

namespace MediaExport.Core.Media.Providers;

/// <summary>Searches a single media source such as YouTube.</summary>
public interface IMediaSearchProvider
{
    /// <summary>A stable identifier, e.g. <c>youtube</c>, used when saving and comparing.</summary>
    string Id { get; }

    /// <summary>The name shown to the user, e.g. <c>YouTube</c>.</summary>
    string DisplayName { get; }

    /// <summary>The resource key of its icon in the app's Resources/Icons.xaml, e.g. <c>YouTubeIcon</c>.</summary>
    string Icon { get; }

    /// <param name="limit">The maximum number of results to return.</param>
    Task<IReadOnlyList<MediaSearchResult>> SearchAsync(string query, int limit = 10, CancellationToken cancellationToken = default);
}
