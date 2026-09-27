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

    private static HttpClient Serve(string json) => new(new FixedResponse(json));

    private sealed class FixedResponse(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }
}
