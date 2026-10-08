// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

namespace MediaExport.Core.Media.Ytdlp;

public sealed record YtdlpSearchResult(
    string Identity,
    string? SourceUrl,
    Uri? ThumbnailUri,
    string Title,
    string? Creator,
    TimeSpan? Duration,
    string? CreatorUrl = null,
    long? Views = null,
    DateTimeOffset? Published = null);
