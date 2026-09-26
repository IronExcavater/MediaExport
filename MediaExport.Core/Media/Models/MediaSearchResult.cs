// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

namespace MediaExport.Core.Media.Models;

public enum MediaKind
{
    Video,
    Audio
}

public sealed record MediaSearchResult(
    string Identity,
    string SourceUrl,
    Uri? ThumbnailUri,
    string Title,
    string? Creator,
    TimeSpan? Duration,
    MediaKind Kind = MediaKind.Video,
    string? CreatorUrl = null,
    Uri? EmbedUri = null);
