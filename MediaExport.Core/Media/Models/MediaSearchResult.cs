// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

namespace MediaExport.Core.Media.Models;

public enum MediaKind
{
    Video,
    Audio
}

/// <summary>One piece of media a source knows about: what a search brings back, and what an export starts from.</summary>
/// <param name="Identity">The source's own name for it, e.g. a video id.</param>
/// <param name="ProviderId">The source it came from, e.g. <c>youtube</c>.</param>
public sealed record MediaSearchResult(
    string Identity,
    string SourceUrl,
    Uri? ThumbnailUri,
    string Title,
    string? Creator,
    TimeSpan? Duration,
    MediaKind Kind = MediaKind.Video,
    string? ProviderId = null)
{
    // List items are announced by screen readers using ToString.
    public override string ToString() => Creator is null ? Title : $"{Title}, {Creator}";
}
