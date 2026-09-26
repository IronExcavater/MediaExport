// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using MediaExport.Core.Media.Ytdlp;

namespace MediaExport.Core.Media.Providers;

public sealed class SoundCloudSearchProvider(YtdlpService ytdlp) : YtdlpSearchProvider(ytdlp)
{
    public override string Id => "soundcloud";
    public override string DisplayName => "SoundCloud";

    protected override string SearchPrefix => "scsearch";
    protected override MediaKind Kind => MediaKind.Audio;

    // The visual widget (artwork backdrop) with everything but playback and the waveform switched off.
    protected override Uri GetEmbedUri(MediaSearchResult media) =>
        new($"https://w.soundcloud.com/player/?url={Uri.EscapeDataString(media.SourceUrl)}"
            + "&visual=true&auto_play=false&hide_related=true&show_comments=false&show_reposts=false"
            + "&show_teaser=false&sharing=false&buying=false&download=false&show_playcount=false");
}
