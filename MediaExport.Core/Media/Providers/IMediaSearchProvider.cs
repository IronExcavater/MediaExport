// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;

namespace MediaExport.Core.Media.Providers;

/// <summary>
/// Somewhere media can be searched for, e.g. YouTube. Each source is a class implementing this, registered in
/// the app's <c>AddMediaExport</c>; everything else takes them all as <c>IEnumerable&lt;IMediaSearchProvider&gt;</c>.
/// </summary>
public interface IMediaSearchProvider
{
    /// <summary>A short name that never changes, e.g. <c>youtube</c>: what's saved and compared.</summary>
    string Id { get; }

    /// <summary>The name people see, e.g. <c>YouTube</c>.</summary>
    string DisplayName { get; }

    /// <param name="limit">The most results needed.</param>
    Task<IReadOnlyList<MediaSearchResult>> SearchAsync(string query, int limit = 10, CancellationToken cancellationToken = default);
}
