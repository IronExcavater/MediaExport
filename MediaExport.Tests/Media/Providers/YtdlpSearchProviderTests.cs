// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using MediaExport.Core.Media.Providers;
using MediaExport.Core.Media.Ytdlp;

namespace MediaExport.Tests.Media.Providers;

public sealed class YtdlpSearchProviderTests
{
    [Test]
    public void YouTube_provider_creates_its_watch_and_embed_urls()
    {
        var provider = new YouTubeSearchProvider(new YtdlpService());
        var result = new YtdlpSearchResult("abc123", null, null, "Result", null, null);

        MediaSearchResult? media = provider.MapSearchResult(result);

        Assert.That(media, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(media!.SourceUrl, Is.EqualTo("https://www.youtube.com/watch?v=abc123"));
            Assert.That(media.EmbedUri, Is.EqualTo(new Uri("https://www.youtube.com/embed/abc123?autoplay=1&rel=0")));
        });
    }

    [Test]
    public void SoundCloud_provider_creates_its_embed_url()
    {
        var provider = new SoundCloudSearchProvider(new YtdlpService());
        var result = new YtdlpSearchResult("track-id", "https://soundcloud.com/artist/track", null, "Track", null, null);

        MediaSearchResult? media = provider.MapSearchResult(result);

        Assert.That(media, Is.Not.Null);
        Assert.That(media!.EmbedUri, Is.EqualTo(new Uri("https://w.soundcloud.com/player/?url=https%3A%2F%2Fsoundcloud.com%2Fartist%2Ftrack&auto_play=true")));
    }
}
