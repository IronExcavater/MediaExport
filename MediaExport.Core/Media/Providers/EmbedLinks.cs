// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;

namespace MediaExport.Core.Media.Providers;

/// <summary>The sources whose own players can show media in the app, and the links to those players.</summary>
public static class EmbedLinks
{
    /// <returns>The link to the media in its source's player, or <c>null</c> for a source without one.</returns>
    public static Uri? For(MediaSearchResult media) => media.ProviderId switch
    {
        // enablejsapi lets the app follow and move the playhead. fs=0 hides the full-screen button, which
        // does nothing in an embedded player.
        "youtube" => new Uri($"https://www.youtube.com/embed/{Uri.EscapeDataString(media.Identity)}"
            + "?rel=0&fs=0&iv_load_policy=3&playsinline=1&enablejsapi=1"),

        // The strip with the waveform, and everything but playback switched off.
        "soundcloud" => new Uri($"https://w.soundcloud.com/player/?url={Uri.EscapeDataString(media.SourceUrl)}"
            + "&visual=false&auto_play=false&hide_related=true&show_comments=false&show_reposts=false"
            + "&show_teaser=false&sharing=false&buying=false&download=false&show_playcount=false"),

        _ => null
    };

    /// <summary>How tall a source's player is when it is a strip, not a picture. <c>null</c> for a 16:9 player.</summary>
    public static double? HeightFor(string? providerId) => providerId == "soundcloud" ? 166 : null;
}
