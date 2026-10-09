// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

namespace MediaExport.Core.Media.Ytdlp;

/// <summary>One entry from a yt-dlp search, before a provider turns it into a media result.</summary>
/// <param name="SourceUrl">The entry's page, where yt-dlp gives one.</param>
public sealed record YtdlpSearchResult(
    string Identity,
    string? SourceUrl,
    Uri? ThumbnailUri,
    string Title,
    string? Creator,
    TimeSpan? Duration,
    long? Views = null,
    DateTimeOffset? Published = null);
