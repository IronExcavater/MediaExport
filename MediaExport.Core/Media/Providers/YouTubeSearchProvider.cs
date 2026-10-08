// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using MediaExport.Core.Media.Ytdlp;

namespace MediaExport.Core.Media.Providers;

public sealed class YouTubeSearchProvider : YtdlpSearchProvider
{
    private readonly YouTubeCharts _charts;

    public YouTubeSearchProvider(YtdlpService ytdlp) : this(ytdlp, new YouTubeCharts(new HttpClient())) { }

    public YouTubeSearchProvider(YtdlpService ytdlp, YouTubeCharts charts) : base(ytdlp) => _charts = charts;

    public override string Id => "youtube";
    public override string DisplayName => "YouTube";
    public override string Icon => "youtube";
    public override bool SupportsPopular => true;

    // Its results say "3 years ago", not a date.
    protected override bool HasApproximateDates => true;

    // The most viewed are read off the site's search page, whose "sp" names the order. It no longer offers newest first.
    protected override string GetSearchTarget(string query, SearchSort sort, int limit) => sort == SearchSort.Popularity
        ? $"https://www.youtube.com/results?search_query={Uri.EscapeDataString(query)}&sp=CAM%253D"
        : $"ytsearch{limit}:{query}";

    protected override string GetSourceUrl(YtdlpSearchResult result) =>
        result.SourceUrl ?? $"https://www.youtube.com/watch?v={Uri.EscapeDataString(result.Identity)}";

    public override async Task<SearchResultPage> PopularAsync(int offset, int take, CancellationToken cancellationToken = default)
    {
        (IReadOnlyList<YouTubeChartEntry> entries, bool hasMore) = await _charts.GetAsync(offset, take, cancellationToken);
        return new SearchResultPage(entries.Select(entry =>
        {
            string url = $"https://www.youtube.com/watch?v={Uri.EscapeDataString(entry.Id)}";
            return new MediaSearchResult(
                entry.Id,
                url,
                entry.Thumbnail,
                entry.Title,
                entry.Creator,
                entry.Duration,
                MediaKind.Video,
                entry.ChannelId is { } channel ? $"https://www.youtube.com/channel/{Uri.EscapeDataString(channel)}" : null,
                EmbedLinks.For(Id, entry.Id, url),
                Id,
                entry.Views);
        }).ToArray(), hasMore);
    }
}
