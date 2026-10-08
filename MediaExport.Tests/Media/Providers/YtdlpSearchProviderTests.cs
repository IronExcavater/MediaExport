// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using MediaExport.Core.Media.Providers;
using MediaExport.Core.Media.Ytdlp;
using System.Net;
using System.Text;

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

    [Test]
    public async Task SoundCloud_popular_feed_uses_the_live_trending_chart()
    {
        var handler = new RouteResponse(request => request.RequestUri!.Host switch
        {
            "soundcloud.com" => """<script src="https://assets.test/app.js"></script>""",
            "assets.test" => """client_id:"0123456789abcdef0123456789abcdef";""",
            _ => """{"collection":[{"track":{"id":42,"permalink_url":"https://soundcloud.com/maker/track","title":"Track","artwork_url":"https://i1.sndcdn.com/artworks-test-large.jpg","duration":185000,"playback_count":1234,"display_date":"2026-10-01T00:00:00Z","publisher_metadata":{"artist":"Artist"},"user":{"username":"Maker","permalink_url":"https://soundcloud.com/maker"}}}]}"""
        });
        var charts = new SoundCloudCharts(new HttpClient(handler));
        var provider = new SoundCloudSearchProvider(YtdlpServiceTests.CreateService(), charts);

        MediaSearchResult result = (await provider.PopularAsync(0, 12)).Results.Single();

        Assert.Multiple(() =>
        {
            Assert.That(result.Title, Is.EqualTo("Track"));
            Assert.That(result.Creator, Is.EqualTo("Artist"));
            Assert.That(result.Views, Is.EqualTo(1234));
            Assert.That(result.Duration, Is.EqualTo(TimeSpan.FromSeconds(185)));
            Assert.That(handler.Requests.Last().Query, Does.Contain("kind=trending").And.Contain("offset=0").And.Contain("limit=12"));
        });
    }

    [TestCase("youtube", "https://www.youtube.com/watch?v=abc", MediaKind.Video)]
    [TestCase("youtubemusic", "https://music.youtube.com/watch?v=abc", MediaKind.Audio)]
    public async Task YouTube_sources_share_the_regional_music_chart(string providerId, string expectedUrl, MediaKind expectedKind)
    {
        int browse = 0;
        var handler = new RouteResponse(_ => browse++ == 0
            ? """{"contents":{"musicTwoRowItemRenderer":{"title":{"runs":[{"text":"Daily Top Music Videos - Australia"}]},"navigationEndpoint":{"browseEndpoint":{"browseId":"VLPLdaily"}}}}}"""
            : """{"contents":{"musicResponsiveListItemRenderer":{"playlistItemData":{"videoId":"abc"},"thumbnail":{"musicThumbnailRenderer":{"thumbnail":{"thumbnails":[{"url":"https://example.test/cover=w120-h120"}]}}},"flexColumns":[{"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[{"text":"Chart song"}]}}},{"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[{"text":"Artist","navigationEndpoint":{"browseEndpoint":{"browseId":"UC1"}}},{"text":" • "},{"text":"Album"},{"text":" • "},{"text":"3:21"}]}}},{"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[{"text":"2.3M plays"}]}}}]}}}""");
        var charts = new YouTubeCharts(new HttpClient(handler));
        IMediaSearchProvider provider = providerId == "youtube"
            ? new YouTubeSearchProvider(YtdlpServiceTests.CreateService(), charts)
            : new YouTubeMusicSearchProvider(new HttpClient(handler), charts);

        MediaSearchResult result = (await provider.PopularAsync(0, 12)).Results.Single();

        Assert.Multiple(() =>
        {
            Assert.That(result.SourceUrl, Is.EqualTo(expectedUrl));
            Assert.That(result.Kind, Is.EqualTo(expectedKind));
            Assert.That(result.Title, Is.EqualTo("Chart song"));
            Assert.That(result.Creator, Is.EqualTo("Artist"));
            Assert.That(result.Views, Is.EqualTo(2_300_000));
        });
    }

    private sealed class RouteResponse(Func<HttpRequestMessage, string> response) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            string mediaType = request.RequestUri!.Host == "soundcloud.com" ? "text/html" : "application/json";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response(request), Encoding.UTF8, mediaType)
            });
        }
    }
}
