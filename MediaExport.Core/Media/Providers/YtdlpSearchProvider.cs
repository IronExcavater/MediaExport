// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using MediaExport.Core.Media.Ytdlp;

namespace MediaExport.Core.Media.Providers;

public abstract class YtdlpSearchProvider(YtdlpService ytdlp) : IMediaSearchProvider
{
    protected abstract string SearchPrefix { get; }

    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public abstract MediaProviderIcon Icon { get; }

    public async Task<IReadOnlyList<MediaSearchResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        IReadOnlyList<YtdlpSearchResult> results = await ytdlp.SearchAsync(
            $"{SearchPrefix}10:{query.Trim()}",
            cancellationToken);

        var media = new List<MediaSearchResult>(results.Count);
        foreach (YtdlpSearchResult result in results)
        {
            MediaSearchResult? mappedResult = MapSearchResult(result);
            if (mappedResult is not null)
                media.Add(mappedResult);
        }

        return media;
    }

    internal MediaSearchResult? MapSearchResult(YtdlpSearchResult result)
    {
        string? sourceUrl = GetSourceUrl(result);
        if (string.IsNullOrWhiteSpace(sourceUrl))
            return null;

        var media = new MediaSearchResult(
            result.Identity,
            sourceUrl,
            result.ThumbnailUri,
            result.Title,
            result.Creator,
            result.Duration);

        return media with { EmbedUri = GetEmbedUri(media) };
    }

    protected virtual string? GetSourceUrl(YtdlpSearchResult result) => result.SourceUrl;

    protected virtual Uri? GetEmbedUri(MediaSearchResult media) => null;
}
