// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using MediaExport.Core.Media.Ytdlp;

namespace MediaExport.Core.Media.Providers;

// Previewed with a native audio player rather than the SoundCloud widget, so it has no embed.
public sealed class SoundCloudSearchProvider(YtdlpService ytdlp) : YtdlpSearchProvider(ytdlp)
{
    public override string Id => "soundcloud";
    public override string DisplayName => "SoundCloud";

    protected override string SearchPrefix => "scsearch";
    protected override MediaKind Kind => MediaKind.Audio;
}
