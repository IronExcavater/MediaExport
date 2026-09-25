// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;

namespace MediaExport.Core.Media.Providers;

public interface IMediaSearchProvider
{
    string Id { get; }
    string DisplayName { get; }
    string IconGlyph { get; }

    Task<IReadOnlyList<MediaSearchResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default);
}
