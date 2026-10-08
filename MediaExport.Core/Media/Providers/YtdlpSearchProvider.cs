// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using MediaExport.Core.Media.Ytdlp;

namespace MediaExport.Core.Media.Providers;

/// <summary>A source searched through yt-dlp, with a search prefix (<c>ytsearch10:…</c>) or a search page's URL.</summary>
public abstract class YtdlpSearchProvider(YtdlpService ytdlp) : IMediaSearchProvider
{
    protected const int ResultCount = 10;

    protected virtual MediaKind Kind => MediaKind.Video;

    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public abstract string Icon { get; }

    /// <summary>What yt-dlp is asked to read for <paramref name="query"/>.</summary>
    protected abstract string GetSearchTarget(string query, SearchSort sort);

    public async Task<IReadOnlyList<MediaSearchResult>> SearchAsync(
        string query,
        SearchSort sort = SearchSort.Relevance,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        IReadOnlyList<YtdlpSearchResult> results = await ytdlp.SearchAsync(GetSearchTarget(query.Trim(), sort), ResultCount, cancellationToken);
        return results.Take(ResultCount).Select(MapSearchResult).OfType<MediaSearchResult>().ToArray();
    }

    internal MediaSearchResult? MapSearchResult(YtdlpSearchResult result)
    {
        string? sourceUrl = GetSourceUrl(result);
        if (string.IsNullOrWhiteSpace(sourceUrl))
            return null;

        return new MediaSearchResult(
            result.Identity,
            sourceUrl,
            result.ThumbnailUri,
            result.Title,
            result.Creator,
            result.Duration,
            Kind,
            result.CreatorUrl,
            EmbedLinks.For(Id, result.Identity, sourceUrl),
            Id,
            result.Views,
            result.Published);
    }

    protected virtual string? GetSourceUrl(YtdlpSearchResult result) => result.SourceUrl;
}
