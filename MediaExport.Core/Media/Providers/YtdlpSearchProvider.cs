// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using MediaExport.Core.Media.Ytdlp;

namespace MediaExport.Core.Media.Providers;

/// <summary>A provider that searches through yt-dlp. A new one names itself and says what yt-dlp should read.</summary>
public abstract class YtdlpSearchProvider(YtdlpService ytdlp) : IMediaSearchProvider
{
    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public abstract string Icon { get; }

    /// <summary>The sites whose links are to this source, such as <c>youtube.com</c>. Their subdomains count too.</summary>
    protected abstract IReadOnlyList<string> Hosts { get; }

    /// <summary>Whether the provider's results are video or audio.</summary>
    protected virtual MediaKind Kind => MediaKind.Video;

    /// <summary>What yt-dlp reads for a query, such as <c>ytsearch10:cats</c> or a search page's URL.</summary>
    /// <param name="count">How many results the search must reach, counting the ones that are skipped.</param>
    protected abstract string GetSearchTarget(string query, int count, SearchSort sort);

    /// <summary>A page listing what is popular on the source, for a search with nothing to search for.</summary>
    protected virtual string? PopularTarget => null;

    /// <summary>Reads what is popular on the source. It is nothing unless there is a <see cref="PopularTarget"/> to read.</summary>
    protected virtual async Task<IReadOnlyList<YtdlpSearchResult>> ReadPopularAsync(int skip, int limit, CancellationToken cancellationToken) =>
        PopularTarget is null ? [] : await ytdlp.SearchAsync(PopularTarget, skip, limit, cancellationToken);

    /// <summary>The page of a result. Override it for a provider whose results don't carry their own URL.</summary>
    protected virtual string? GetSourceUrl(YtdlpSearchResult result) => result.SourceUrl;

    public async Task<IReadOnlyList<MediaSearchResult>> SearchAsync(string query, int skip = 0, int limit = 10, SearchSort sort = SearchSort.Relevance, CancellationToken cancellationToken = default)
    {
        var isSearch = !string.IsNullOrWhiteSpace(query);
        var found = isSearch
            ? await ytdlp.SearchAsync(GetSearchTarget(query.Trim(), skip + limit, sort), skip, limit, cancellationToken)
            : await ReadPopularAsync(skip, limit, cancellationToken);

        return found.Select(ToMedia).OfType<MediaSearchResult>().ToList();
    }

    public async Task<MediaSearchResult?> FindByLinkAsync(Uri link, CancellationToken cancellationToken = default)
    {
        var isToThisSource = Hosts.Any(host => link.Host == host || link.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase));
        if (!isToThisSource)
            return null;

        var found = await ytdlp.ReadAsync(link.AbsoluteUri, cancellationToken);
        return found is null ? null : ToMedia(found);
    }

    // A result without a page can't be opened or exported, so it isn't one.
    private MediaSearchResult? ToMedia(YtdlpSearchResult result)
    {
        var sourceUrl = GetSourceUrl(result);
        if (sourceUrl is null)
            return null;

        return new MediaSearchResult(
            result.Identity,
            sourceUrl,
            result.ThumbnailUri,
            result.Title,
            result.Creator,
            result.Duration,
            Kind,
            Id,
            result.Views,
            result.Published,
            result.CreatorUri);
    }
}
