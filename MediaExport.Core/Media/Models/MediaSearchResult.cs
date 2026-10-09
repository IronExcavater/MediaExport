// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

namespace MediaExport.Core.Media.Models;

public enum MediaKind
{
    Video,
    Audio
}

/// <summary>A media item returned by a provider and available for export.</summary>
/// <param name="Identity">The provider's own identifier for the item, e.g. a video id.</param>
/// <param name="ProviderId">The <see cref="Providers.IMediaSearchProvider.Id"/> of the provider it came from.</param>
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
    // Screen readers announce list items by their ToString.
    public override string ToString() => Creator is null ? Title : $"{Title}, {Creator}";
}
