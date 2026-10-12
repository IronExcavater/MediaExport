// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Ytdlp;

namespace MediaExport.Core.Media.Providers;

public sealed class YouTubeSearchProvider(YtdlpService ytdlp) : YtdlpSearchProvider(ytdlp)
{
    public override string Id => "youtube";
    public override string DisplayName => "YouTube";
    public override string Icon => "YouTubeIcon";

    protected override IReadOnlyList<string> Hosts => ["youtube.com", "youtu.be"];

    // YouTube's chart of the most watched music videos worldwide. Its general Trending page no longer exists.
    protected override string PopularTarget => "https://www.youtube.com/playlist?list=PL4fGSI1pDJn6puJdseH2Rt9sMvt9E2M4i";

    // yt-dlp's own search can't be ranked, so a search by views reads YouTube's results page, sorted by views.
    // YouTube no longer sorts by upload date, which is why there is no ranking for the newest.
    protected override string GetSearchTarget(string query, int count, SearchSort sort) => sort == SearchSort.Views
        ? $"https://www.youtube.com/results?search_query={Uri.EscapeDataString(query)}&sp=CAM%3D"
        : $"ytsearch{count}:{query}";

    protected override LinkKind ClassifyLink(Uri link)
    {
        var path = link.AbsolutePath;
        string[] creatorPaths = ["/@", "/channel/", "/c/", "/user/"];
        if (creatorPaths.Any(path.StartsWith))
            return LinkKind.Creator;

        // A video that is also in a playlist has both in its address, and is the video.
        return path.StartsWith("/playlist") ? LinkKind.List : LinkKind.Item;
    }

    protected override string GetSourceUrl(YtdlpSearchResult result) =>
        result.SourceUrl ?? $"https://www.youtube.com/watch?v={Uri.EscapeDataString(result.Identity)}";
}
