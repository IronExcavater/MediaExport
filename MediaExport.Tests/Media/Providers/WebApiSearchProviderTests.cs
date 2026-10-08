// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using MediaExport.Core.Media.Providers;
using System.Net;
using System.Text;

namespace MediaExport.Tests.Media.Providers;

public sealed class WebApiSearchProviderTests
{
    [Test]
    public async Task Dailymotion_results_become_embeddable_videos()
    {
        var provider = new DailymotionSearchProvider(Serve("""
            {"list":[{"id":"x9","title":"Clip","duration":240,"thumbnail_480_url":"https://example.test/t.jpg","owner.screenname":"Maker","owner.url":"https://www.dailymotion.com/maker","url":"https://www.dailymotion.com/video/x9"}]}
            """));

        MediaSearchResult result = (await provider.SearchAsync("clip")).Single();

        Assert.Multiple(() =>
        {
            Assert.That(result.Creator, Is.EqualTo("Maker"));
            Assert.That(result.Duration, Is.EqualTo(TimeSpan.FromMinutes(4)));
            Assert.That(result.EmbedUri, Is.EqualTo(new Uri("https://geo.dailymotion.com/player.html?video=x9")));
            Assert.That(result.ProviderId, Is.EqualTo("dailymotion"));
        });
    }

    [Test]
    public async Task Internet_Archive_items_are_audio_or_video_by_their_type()
    {
        var provider = new InternetArchiveSearchProvider(Serve("""
            {"response":{"docs":[{"identifier":"tape","title":"Tape","creator":["Band","Label"],"mediatype":"audio"},{"identifier":"reel","title":"&#34;Reel&#34;","mediatype":"movies"}]}}
            """));

        IReadOnlyList<MediaSearchResult> results = await provider.SearchAsync("old");

        Assert.Multiple(() =>
        {
            Assert.That(results.Select(result => result.Kind), Is.EqualTo(new[] { MediaKind.Audio, MediaKind.Video }));
            Assert.That(results[0].Creator, Is.EqualTo("Band"));
            Assert.That(results[0].ThumbnailUri, Is.EqualTo(new Uri("https://archive.org/services/img/tape")));
            Assert.That(results[1].Title, Is.EqualTo("\"Reel\""));
            Assert.That(results[1].EmbedUri, Is.EqualTo(new Uri("https://archive.org/embed/reel")));
        });
    }

    [Test]
    public async Task Vimeo_searches_with_the_visitor_token()
    {
        var handler = new FixedResponse("""
            {"token":"abc","data":[{"uri":"/videos/42","name":"Short","link":"https://vimeo.com/42","duration":90,"pictures":{"sizes":[{"width":100,"link":"https://example.test/small.jpg"},{"width":640,"link":"https://example.test/wide.jpg"}]},"user":{"name":"Maker","link":"https://vimeo.com/maker"}}]}
            """);
        var provider = new VimeoSearchProvider(new HttpClient(handler));

        MediaSearchResult result = (await provider.SearchAsync("short")).Single();
        await provider.SearchAsync("again");

        Assert.Multiple(() =>
        {
            Assert.That(handler.Requests, Is.EqualTo(new[] { "vimeo.com ", "api.vimeo.com jwt abc", "api.vimeo.com jwt abc" }), "the token is asked for once");
            Assert.That(result.Identity, Is.EqualTo("42"));
            Assert.That(result.Creator, Is.EqualTo("Maker"));
            Assert.That(result.ThumbnailUri, Is.EqualTo(new Uri("https://example.test/wide.jpg")));
            Assert.That(result.EmbedUri, Is.EqualTo(new Uri("https://player.vimeo.com/video/42")));
        });
    }

    [Test]
    public async Task YouTube_Music_songs_name_their_artists_and_play_as_YouTube_videos()
    {
        var provider = new YouTubeMusicSearchProvider(Serve("""
            {"contents":{"tabbedSearchResultsRenderer":{"tabs":[{"tabRenderer":{"content":{"sectionListRenderer":{"contents":[{"itemSectionRenderer":{}},{"musicShelfRenderer":{"contents":[{"musicResponsiveListItemRenderer":{
              "playlistItemData":{"videoId":"abc"},
              "thumbnail":{"musicThumbnailRenderer":{"thumbnail":{"thumbnails":[{"url":"https://example.test/cover=w60-h60-l90-rj"},{"url":"https://example.test/cover=w120-h120-l90-rj"}]}}},
              "flexColumns":[
                {"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[{"text":"Song"}]}}},
                {"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[{"text":"One","navigationEndpoint":{"browseEndpoint":{"browseId":"UC1"}}},{"text":" & "},{"text":"Two"},{"text":" • "},{"text":"Album"},{"text":" • "},{"text":"4:09"}]}}}]}}]}}]}}}}]}}}
            """));

        MediaSearchResult result = (await provider.SearchAsync("song")).Single();

        Assert.Multiple(() =>
        {
            Assert.That(result.Title, Is.EqualTo("Song"));
            Assert.That(result.Creator, Is.EqualTo("One & Two"));
            Assert.That(result.CreatorUrl, Is.EqualTo("https://music.youtube.com/channel/UC1"));
            Assert.That(result.Duration, Is.EqualTo(new TimeSpan(0, 4, 9)));
            Assert.That(result.ThumbnailUri, Is.EqualTo(new Uri("https://example.test/cover=w480-h480-l90-rj")));
            Assert.That(result.EmbedUri?.AbsoluteUri, Does.StartWith("https://www.youtube.com/embed/abc?"));
            Assert.That(result.Kind, Is.EqualTo(MediaKind.Audio));
        });
    }

    [Test]
    public async Task Mixcloud_shows_embed_by_their_path()
    {
        var provider = new MixcloudSearchProvider(Serve("""
            {"data":[{"key":"/dj/set/","url":"https://www.mixcloud.com/dj/set/","name":"Set","audio_length":3600,"pictures":{"640wx640h":"https://example.test/set.jpg"},"user":{"name":"DJ","url":"https://www.mixcloud.com/dj/"}}]}
            """));

        MediaSearchResult result = (await provider.SearchAsync("set")).Single();

        Assert.Multiple(() =>
        {
            Assert.That(result.Creator, Is.EqualTo("DJ"));
            Assert.That(result.Duration, Is.EqualTo(TimeSpan.FromHours(1)));
            Assert.That(result.EmbedUri, Is.EqualTo(new Uri("https://player-widget.mixcloud.com/widget/iframe/?hide_cover=1&feed=%2Fdj%2Fset%2F")));
        });
    }

    [Test]
    public async Task Bilibili_searches_as_a_visitor_and_tidies_what_comes_back()
    {
        var handler = new FixedResponse("""
            {"data":{"b_3":"visitor","result":[{"bvid":"BV1","title":"A <em class=\"keyword\">Clip</em> &amp; more","author":"Maker","mid":7,"pic":"//example.test/p.jpg","duration":"125:30"}]}}
            """);
        var provider = new BilibiliSearchProvider(new HttpClient(handler));

        MediaSearchResult result = (await provider.SearchAsync("clip")).Single();
        await provider.SearchAsync("again");

        Assert.Multiple(() =>
        {
            Assert.That(handler.Requests, Has.Count.EqualTo(3), "the visitor id is asked for once");
            Assert.That(result.Title, Is.EqualTo("A Clip & more"));
            Assert.That(result.Duration, Is.EqualTo(new TimeSpan(2, 5, 30)));
            Assert.That(result.ThumbnailUri, Is.EqualTo(new Uri("https://example.test/p.jpg")));
            Assert.That(result.CreatorUrl, Is.EqualTo("https://space.bilibili.com/7"));
            Assert.That(result.EmbedUri, Is.EqualTo(new Uri("https://player.bilibili.com/player.html?bvid=BV1&autoplay=0")));
        });
    }

    [Test]
    public async Task Niconico_titles_are_decoded_twice()
    {
        var provider = new NiconicoSearchProvider(Serve("""
            {"data":[{"contentId":"sm9","title":"It&amp;#039;s","lengthSeconds":124,"thumbnailUrl":"https://example.test/9"}]}
            """));

        MediaSearchResult result = (await provider.SearchAsync("it")).Single();

        Assert.Multiple(() =>
        {
            Assert.That(result.Title, Is.EqualTo("It's"));
            Assert.That(result.SourceUrl, Is.EqualTo("https://www.nicovideo.jp/watch/sm9"));
            Assert.That(result.EmbedUri, Is.EqualTo(new Uri("https://embed.nicovideo.jp/watch/sm9")));
        });
    }

    [Test]
    public async Task Sources_that_can_order_their_own_results_are_asked_to()
    {
        var handler = new FixedResponse("{}");
        var http = new HttpClient(handler);

        await new DailymotionSearchProvider(http).SearchAsync("clip", SearchSort.Popularity);
        await new InternetArchiveSearchProvider(http).SearchAsync("clip", SearchSort.Popularity);
        await new NiconicoSearchProvider(http).SearchAsync("clip");

        Assert.That(handler.Queries, Has.Exactly(1).Contains("sort=visited")
            .And.Exactly(1).Contains("sort[]=downloads+desc")
            .And.Exactly(1).Contains("_sort=-viewCounter"));
    }

    [TestCase("1.9B plays", 1_900_000_000)]
    [TestCase("727K plays", 727_000)]
    [TestCase("812 plays", 812)]
    [TestCase("Album", null)]
    public void YouTube_Music_plays_are_read_from_their_short_form(string text, long? plays) =>
        Assert.That(YouTubeMusicSearchProvider.ReadPlays(text), Is.EqualTo(plays));

    private static HttpClient Serve(string json) => new(new FixedResponse(json));

    private sealed class FixedResponse(string json) : HttpMessageHandler
    {
        /// <summary>Each request's host and what it authorised with.</summary>
        public List<string> Requests { get; } = [];

        public List<string> Queries { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add($"{request.RequestUri!.Host} {request.Headers.Authorization}");
            Queries.Add(Uri.UnescapeDataString(request.RequestUri.Query));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
}
