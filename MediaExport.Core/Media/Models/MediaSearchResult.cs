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
/// <param name="Views">How often it has been viewed, where the provider says.</param>
/// <param name="Published">When it was published, where the provider says.</param>
/// <param name="CreatorUri">The creator's page on the source, such as their channel, where the provider says.</param>
public sealed record MediaSearchResult(
    string Identity,
    string SourceUrl,
    Uri? ThumbnailUri,
    string Title,
    string? Creator,
    TimeSpan? Duration,
    MediaKind Kind = MediaKind.Video,
    string? ProviderId = null,
    long? Views = null,
    DateTimeOffset? Published = null,
    Uri? CreatorUri = null)
{
    // Screen readers announce list items by their ToString.
    public override string ToString() => Creator is null ? Title : $"{Title}, {Creator}";
}
