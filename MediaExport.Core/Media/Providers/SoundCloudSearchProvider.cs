// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using MediaExport.Core.Media.Ytdlp;

namespace MediaExport.Core.Media.Providers;

public sealed class SoundCloudSearchProvider(YtdlpService ytdlp, SoundCloudCharts charts) : YtdlpSearchProvider(ytdlp)
{
    public override string Id => "soundcloud";
    public override string DisplayName => "SoundCloud";
    public override string Icon => "SoundCloudIcon";

    protected override IReadOnlyList<string> Hosts => ["soundcloud.com"];
    protected override MediaKind Kind => MediaKind.Audio;

    // yt-dlp's SoundCloud search has one ranking, its own, so the ranking asked for is not applied.
    protected override string GetSearchTarget(string query, int count, SearchSort sort) => $"scsearch{count}:{query}";

    // yt-dlp lists the chart's tracks without their names, so the chart is read from SoundCloud's own website.
    protected override Task<IReadOnlyList<YtdlpSearchResult>> ReadPopularAsync(int skip, int limit, CancellationToken cancellationToken) =>
        charts.ReadAsync(skip, limit, cancellationToken);
}
