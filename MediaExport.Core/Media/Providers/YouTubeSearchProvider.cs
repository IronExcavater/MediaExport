// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using MediaExport.Core.Media.Ytdlp;

namespace MediaExport.Core.Media.Providers;

public sealed class YouTubeSearchProvider(YtdlpService ytdlp) : YtdlpSearchProvider(ytdlp)
{
    public override string Id => "youtube";
    public override string DisplayName => "YouTube";

    protected override string SearchPrefix => "ytsearch";

    protected override string GetSourceUrl(YtdlpSearchResult result) =>
        result.SourceUrl ?? $"https://www.youtube.com/watch?v={Uri.EscapeDataString(result.Identity)}";

    // Minimal chrome: no fullscreen button (fs=0), annotations or unrelated suggestions.
    protected override Uri GetEmbedUri(MediaSearchResult media) =>
        new($"https://www.youtube.com/embed/{Uri.EscapeDataString(media.Identity)}?rel=0&fs=0&iv_load_policy=3&playsinline=1");
}
