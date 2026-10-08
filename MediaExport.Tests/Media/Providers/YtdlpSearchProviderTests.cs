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
        var provider = new YouTubeSearchProvider(YtdlpServiceTests.CreateService());
        var result = new YtdlpSearchResult("abc123", null, null, "Result", null, null, "https://www.youtube.com/@channel");

        MediaSearchResult? media = provider.MapSearchResult(result);

        Assert.That(media, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(media!.SourceUrl, Is.EqualTo("https://www.youtube.com/watch?v=abc123"));
            Assert.That(media.EmbedUri?.AbsoluteUri, Does.StartWith("https://www.youtube.com/embed/abc123?"));
            Assert.That(media.EmbedUri?.Query, Does.Contain("enablejsapi=1"));
            Assert.That(media.CreatorUrl, Is.EqualTo("https://www.youtube.com/@channel"));
            Assert.That(media.ProviderId, Is.EqualTo("youtube"));
        });
    }

    [Test]
    public void SoundCloud_provider_returns_audio_with_its_strip_of_a_player()
    {
        var provider = new SoundCloudSearchProvider(YtdlpServiceTests.CreateService());
        var result = new YtdlpSearchResult("track-id", "https://soundcloud.com/artist/track", null, "Track", null, null);

        MediaSearchResult? media = provider.MapSearchResult(result);

        Assert.That(media, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(media!.Kind, Is.EqualTo(MediaKind.Audio));
            Assert.That(media.ProviderId, Is.EqualTo("soundcloud"));
            Assert.That(media.EmbedUri?.AbsoluteUri, Does.StartWith("https://w.soundcloud.com/player/?url=https%3A%2F%2Fsoundcloud.com%2Fartist%2Ftrack&visual=false&auto_play=false"));
            Assert.That(EmbedLinks.HeightFor(media.ProviderId), Is.EqualTo(166));
            Assert.That(EmbedLinks.HeightFor("youtube"), Is.Null, "pictures stay 16:9");
        });
    }
}
