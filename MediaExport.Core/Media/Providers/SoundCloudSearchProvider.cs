// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using MediaExport.Core.Media.Ytdlp;

namespace MediaExport.Core.Media.Providers;

public sealed class SoundCloudSearchProvider : YtdlpSearchProvider
{
    private readonly SoundCloudCharts _charts;

    public SoundCloudSearchProvider(YtdlpService ytdlp) : this(ytdlp, new SoundCloudCharts(new HttpClient())) { }

    public SoundCloudSearchProvider(YtdlpService ytdlp, SoundCloudCharts charts) : base(ytdlp) => _charts = charts;

    public override string Id => "soundcloud";
    public override string DisplayName => "SoundCloud";
    public override string Icon => "soundcloud";
    public override bool SupportsPopular => true;

    protected override MediaKind Kind => MediaKind.Audio;

    // Its search has one order.
    protected override string GetSearchTarget(string query, SearchSort sort, int limit) => $"scsearch{limit}:{query}";

    public override async Task<SearchResultPage> PopularAsync(int offset, int take, CancellationToken cancellationToken = default)
    {
        (IReadOnlyList<SoundCloudChartEntry> entries, bool hasMore) = await _charts.GetAsync(offset, take, cancellationToken);
        return new SearchResultPage(entries.Select(entry => new MediaSearchResult(
            entry.Id,
            entry.Url,
            entry.Thumbnail,
            entry.Title,
            entry.Creator,
            entry.Duration,
            MediaKind.Audio,
            entry.CreatorUrl,
            EmbedLinks.For(Id, entry.Id, entry.Url),
            Id,
            entry.Views,
            entry.Published)).ToArray(), hasMore);
    }
}
