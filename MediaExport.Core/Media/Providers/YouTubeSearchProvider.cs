// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Ytdlp;

namespace MediaExport.Core.Media.Providers;

public class YouTubeSearchProvider(YtdlpService ytdlp) : YtdlpSearchProvider(ytdlp)
{
    public override string Id => "youtube";
    public override string DisplayName => "YouTube";
    public override string Icon => "YouTubeIcon";

    // yt-dlp's own search can't be ranked, so a search by views reads YouTube's results page, sorted by views.
    protected override string GetSearchTarget(string query, int count, SearchSort sort) => sort == SearchSort.Views
        ? $"https://www.youtube.com/results?search_query={Uri.EscapeDataString(query)}&sp=CAM%3D"
        : $"ytsearch{count}:{query}";

    protected override string GetSourceUrl(YtdlpSearchResult result) =>
        result.SourceUrl ?? $"https://www.youtube.com/watch?v={Uri.EscapeDataString(result.Identity)}";
}

// A second copy of YouTube, so that choosing several sources has a choice until there is a real second source.
public sealed class YouTubeCopySearchProvider(YtdlpService ytdlp) : YouTubeSearchProvider(ytdlp)
{
    public override string Id => "youtube2";
    public override string DisplayName => "YouTube (copy)";
}
