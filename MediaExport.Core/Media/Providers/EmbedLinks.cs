// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

namespace MediaExport.Core.Media.Providers;

/// <summary>The sites whose own players can show media in the app, and their embed links.</summary>
public static class EmbedLinks
{
    /// <returns><c>null</c> for sites without an embeddable player.</returns>
    public static Uri? For(string? providerId, string identity, string sourceUrl) => providerId switch
    {
        // enablejsapi lets the app follow and move the playhead; fs=0 hides fullscreen, which embeds can't do here.
        "youtube" or "youtubemusic" => new Uri($"https://www.youtube.com/embed/{Uri.EscapeDataString(identity)}"
            + "?rel=0&fs=0&iv_load_policy=3&playsinline=1&enablejsapi=1"),

        // The strip with the waveform, and everything but playback switched off.
        "soundcloud" => new Uri($"https://w.soundcloud.com/player/?url={Uri.EscapeDataString(sourceUrl)}"
            + "&visual=false&auto_play=false&hide_related=true&show_comments=false&show_reposts=false"
            + "&show_teaser=false&sharing=false&buying=false&download=false&show_playcount=false"),

        // The show is named by its path on the site: /uploader/show/
        "mixcloud" when Uri.TryCreate(sourceUrl, UriKind.Absolute, out Uri? show) =>
            new Uri($"https://player-widget.mixcloud.com/widget/iframe/?hide_cover=1&feed={Uri.EscapeDataString(show.AbsolutePath)}"),

        "vimeo" => new Uri($"https://player.vimeo.com/video/{Uri.EscapeDataString(identity)}"),

        "dailymotion" => new Uri($"https://geo.dailymotion.com/player.html?video={Uri.EscapeDataString(identity)}"),

        "bilibili" => new Uri($"https://player.bilibili.com/player.html?bvid={Uri.EscapeDataString(identity)}&autoplay=0"),

        "niconico" => new Uri($"https://embed.nicovideo.jp/watch/{Uri.EscapeDataString(identity)}"),

        "internetarchive" => new Uri($"https://archive.org/embed/{Uri.EscapeDataString(identity)}"),

        _ => null
    };

    /// <summary>Whether a site's player says where it is and can be moved, so the app's timeline can follow and steer it.</summary>
    public static bool FollowsPlayhead(string? providerId) => providerId is "youtube" or "youtubemusic" or "soundcloud";

    /// <summary>How tall a site's player is when it's a strip rather than a picture; <c>null</c> for players that are 16:9.</summary>
    public static double? HeightFor(string? providerId) => providerId switch
    {
        "soundcloud" => 166,
        "mixcloud" => 60,
        _ => null
    };
}
