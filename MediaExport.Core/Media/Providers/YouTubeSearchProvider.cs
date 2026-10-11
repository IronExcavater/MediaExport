// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Ytdlp;

namespace MediaExport.Core.Media.Providers;

public sealed class YouTubeSearchProvider(YtdlpService ytdlp) : YtdlpSearchProvider(ytdlp)
{
    public override string Id => "youtube";
    public override string DisplayName => "YouTube";
    public override string Icon => "YouTubeIcon";

    protected override string GetSearchTarget(string query, int count) => $"ytsearch{count}:{query}";

    protected override string GetSourceUrl(YtdlpSearchResult result) =>
        result.SourceUrl ?? $"https://www.youtube.com/watch?v={Uri.EscapeDataString(result.Identity)}";
}
