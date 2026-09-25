// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using MediaExport.Core.Media.Ytdlp;

namespace MediaExport.Core.Media.Providers;

public sealed class SoundCloudSearchProvider(YtdlpService ytdlp) : YtdlpSearchProvider(ytdlp)
{
    public override string Id => "soundcloud";
    public override string DisplayName => "SoundCloud";
    public override MediaProviderIcon Icon => MediaProviderIcon.SoundCloud;

    protected override string SearchPrefix => "scsearch";

    protected override Uri? GetEmbedUri(MediaSearchResult media) =>
        Uri.TryCreate(media.SourceUrl, UriKind.Absolute, out _)
            ? new Uri($"https://w.soundcloud.com/player/?url={Uri.EscapeDataString(media.SourceUrl)}&auto_play=true")
            : null;
}
