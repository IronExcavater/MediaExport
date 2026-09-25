// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using MediaExport.Core.Media.Ytdlp;

namespace MediaExport.Core.Media.Providers;

public sealed class YouTubeSearchProvider(YtdlpService ytdlp) : IMediaSearchProvider
{
    public string Id => "youtube";
    public string DisplayName => "YouTube";
    public string IconGlyph => "\uE768";

    public Task<IReadOnlyList<MediaSearchResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default) =>
        ytdlp.SearchAsync($"ytsearch10:{query.Trim()}", cancellationToken);
}
