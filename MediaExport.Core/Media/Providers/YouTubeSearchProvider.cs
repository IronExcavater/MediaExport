// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Ytdlp;

namespace MediaExport.Core.Media.Providers;

public sealed class YouTubeSearchProvider(YtdlpService ytdlp) : YtdlpSearchProvider(ytdlp)
{
    public override string Id => "youtube";
    public override string DisplayName => "YouTube";
    public override string Icon => "youtube";

    // The most played are read off the site's search page, whose "sp" names the order. It no longer offers newest first.
    protected override string GetSearchTarget(string query, SearchSort sort) => sort == SearchSort.Popularity
        ? $"https://www.youtube.com/results?search_query={Uri.EscapeDataString(query)}&sp=CAM%253D"
        : $"ytsearch{ResultCount}:{query}";

    protected override string GetSourceUrl(YtdlpSearchResult result) =>
        result.SourceUrl ?? $"https://www.youtube.com/watch?v={Uri.EscapeDataString(result.Identity)}";
}
