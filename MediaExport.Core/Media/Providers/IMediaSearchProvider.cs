// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;

namespace MediaExport.Core.Media.Providers;

/// <summary>Searches a single media source such as YouTube.</summary>
public interface IMediaSearchProvider
{
    /// <summary>A short name that never changes, e.g. <c>youtube</c>: what's saved and compared.</summary>
    string Id { get; }

    /// <summary>The name people see, e.g. <c>YouTube</c>.</summary>
    string DisplayName { get; }

    /// <summary>The name of its icon in the app's Resources/Icons.xaml, e.g. <c>YouTubeIcon</c>.</summary>
    string Icon { get; }

    /// <param name="limit">The most results needed.</param>
    Task<IReadOnlyList<MediaSearchResult>> SearchAsync(string query, int limit = 10, CancellationToken cancellationToken = default);
}
