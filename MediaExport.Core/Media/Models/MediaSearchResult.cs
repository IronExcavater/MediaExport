// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

namespace MediaExport.Core.Media.Models;

public enum MediaKind
{
    Video,
    Audio
}

/// <param name="ProviderId">The source it came from, e.g. <c>youtube</c>; recent results are kept per source.</param>
public sealed record MediaSearchResult(
    string Identity,
    string SourceUrl,
    Uri? ThumbnailUri,
    string Title,
    string? Creator,
    TimeSpan? Duration,
    MediaKind Kind = MediaKind.Video,
    string? CreatorUrl = null,
    Uri? EmbedUri = null,
    string? ProviderId = null)
{
    // List items are announced by screen readers using ToString.
    public override string ToString() => Creator is null ? Title : $"{Title}, {Creator}";
}
