// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using MediaExport.Core.Media.Ytdlp;

namespace MediaExport.Core.Media.Providers;

/// <summary>A source searched through yt-dlp, with a search prefix (<c>ytsearch10:…</c>) or a search page's URL.</summary>
public abstract class YtdlpSearchProvider(YtdlpService ytdlp) : IMediaSearchProvider
{
    protected virtual MediaKind Kind => MediaKind.Video;

    /// <summary>Whether the source's results give dates only to about a year.</summary>
    protected virtual bool HasApproximateDates => false;

    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public abstract string Icon { get; }
    public bool SupportsPaging => true;
    public virtual bool SupportsPopular => PopularTarget is not null;

    /// <summary>What yt-dlp is asked to read for <paramref name="query"/>.</summary>
    protected abstract string GetSearchTarget(string query, SearchSort sort, int limit);

    protected virtual string? PopularTarget => null;

    public async Task<IReadOnlyList<MediaSearchResult>> SearchAsync(
        string query,
        SearchSort sort = SearchSort.Relevance,
        CancellationToken cancellationToken = default,
        int limit = 10)
    {
        return (await SearchAsync(query, sort, offset: 0, take: limit, cancellationToken)).Results;
    }

    public async Task<SearchResultPage> SearchAsync(
        string query,
        SearchSort sort,
        int offset,
        int take,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new SearchResultPage([], false);

        offset = Math.Max(0, offset);
        take = Math.Max(1, take);
        IReadOnlyList<YtdlpSearchResult> results = await ytdlp.SearchAsync(
            GetSearchTarget(query.Trim(), sort, offset + take + 1), offset, take + 1, cancellationToken);
        MediaSearchResult[] mapped = results.Select(MapSearchResult).OfType<MediaSearchResult>().ToArray();
        return new SearchResultPage(mapped.Take(take).ToArray(), mapped.Length > take);
    }

    public virtual async Task<SearchResultPage> PopularAsync(int offset, int take, CancellationToken cancellationToken = default)
    {
        if (PopularTarget is not { } target)
            return new SearchResultPage([], false);

        offset = Math.Max(0, offset);
        take = Math.Max(1, take);
        IReadOnlyList<YtdlpSearchResult> results = await ytdlp.SearchAsync(target, offset, take + 1, cancellationToken);
        MediaSearchResult[] mapped = results.Select(MapSearchResult).OfType<MediaSearchResult>().ToArray();
        return new SearchResultPage(mapped.Take(take).ToArray(), mapped.Length > take);
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
            result.Published,
            HasApproximateDates);
    }

    protected virtual string? GetSourceUrl(YtdlpSearchResult result) => result.SourceUrl;
}
