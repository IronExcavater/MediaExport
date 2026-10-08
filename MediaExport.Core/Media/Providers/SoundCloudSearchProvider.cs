// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using MediaExport.Core.Media.Ytdlp;

namespace MediaExport.Core.Media.Providers;

public sealed class SoundCloudSearchProvider(YtdlpService ytdlp) : YtdlpSearchProvider(ytdlp)
{
    public override string Id => "soundcloud";
    public override string DisplayName => "SoundCloud";
    public override string Icon => "soundcloud";

    protected override MediaKind Kind => MediaKind.Audio;

    // Its search has one order.
    protected override string GetSearchTarget(string query, SearchSort sort) => $"scsearch{ResultCount}:{query}";
}
