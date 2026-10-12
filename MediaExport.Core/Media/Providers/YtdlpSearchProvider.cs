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

    /// <summary>Whether the provider's results are video or audio.</summary>
    protected virtual MediaKind Kind => MediaKind.Video;

    /// <summary>What yt-dlp reads for a query, such as <c>ytsearch10:cats</c> or a search page's URL.</summary>
    /// <param name="count">How many results the search must reach, counting the ones that are skipped.</param>
    protected abstract string GetSearchTarget(string query, int count, SearchSort sort);

    /// <summary>The page of a result. Override it for a provider whose results don't carry their own URL.</summary>
    protected virtual string? GetSourceUrl(YtdlpSearchResult result) => result.SourceUrl;

    public async Task<IReadOnlyList<MediaSearchResult>> SearchAsync(string query, int skip = 0, int limit = 10, SearchSort sort = SearchSort.Relevance, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var target = GetSearchTarget(query.Trim(), skip + limit, sort);
        var found = await ytdlp.SearchAsync(target, skip, limit, cancellationToken);

        var results = new List<MediaSearchResult>();
        foreach (var result in found)
        {
            // A result without a page can't be opened or exported.
            var sourceUrl = GetSourceUrl(result);
            if (sourceUrl is null)
                continue;

            results.Add(new MediaSearchResult(
                result.Identity,
                sourceUrl,
                result.ThumbnailUri,
                result.Title,
                result.Creator,
                result.Duration,
                Kind,
                Id,
                result.Views,
                result.Published));
        }
        return results;
    }
}
