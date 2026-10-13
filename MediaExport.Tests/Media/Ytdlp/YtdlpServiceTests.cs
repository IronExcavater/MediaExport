// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Ytdlp;

namespace MediaExport.Tests.Media.Ytdlp;

public class YtdlpServiceTests
{
    [Test]
    public void SearchResultsAreReadFromYtdlpsJson()
    {
        const string json = """
            {
              "entries": [
                {
                  "id": "abc123",
                  "title": "A song",
                  "url": "https://www.youtube.com/watch?v=abc123",
                  "channel": "An artist",
                  "duration": 187.0,
                  "view_count": 1900000,
                  "timestamp": 1700000000,
                  "thumbnails": [
                    { "url": "https://i.ytimg.com/small.jpg" },
                    { "url": "https://i.ytimg.com/large.jpg" }
                  ]
                }
              ]
            }
            """;

        var result = YtdlpService.ReadSearchResults(json).Single();

        Assert.Multiple(() =>
        {
            Assert.That(result.Identity, Is.EqualTo("abc123"));
            Assert.That(result.Title, Is.EqualTo("A song"));
            Assert.That(result.SourceUrl, Is.EqualTo("https://www.youtube.com/watch?v=abc123"));
            Assert.That(result.Creator, Is.EqualTo("An artist"));
            Assert.That(result.Duration, Is.EqualTo(TimeSpan.FromSeconds(187)));
            Assert.That(result.Views, Is.EqualTo(1_900_000));
            Assert.That(result.Published, Is.EqualTo(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000)));
            Assert.That(result.ThumbnailUri, Is.EqualTo(new Uri("https://i.ytimg.com/large.jpg")), "the largest is listed last");
        });
    }

    [Test]
    public void EntriesThatArentMediaAreLeftOut()
    {
        const string json = """
            {
              "entries": [
                { "id": "channel1", "title": "A channel", "ie_key": "YoutubeTab" },
                { "id": "untitled", "title": null },
                { "id": "abc123", "title": "A song", "duration": null, "view_count": null }
              ]
            }
            """;

        var results = YtdlpService.ReadSearchResults(json);

        Assert.Multiple(() =>
        {
            Assert.That(results.Select(result => result.Identity), Is.EqualTo(new[] { "abc123" }));
            Assert.That(results[0].Duration, Is.Null, "a value written as null counts as missing");
            Assert.That(results[0].Views, Is.Null);
        });
    }

    [Test]
    public void ASearchReadsOnlyThePageAskedFor()
    {
        var arguments = YtdlpService.GetSearchArguments("ytsearch5:cats", skip: 0, limit: 5);
        var thirdPage = YtdlpService.GetSearchArguments("ytsearch60:cats", skip: 40, limit: 20);

        Assert.Multiple(() =>
        {
            Assert.That(arguments, Does.Contain("--flat-playlist"), "entries are listed without opening each one");
            Assert.That(arguments.SkipWhile(argument => argument != "--playlist-end").Skip(1).First(), Is.EqualTo("5"));
            Assert.That(arguments.Last(), Is.EqualTo("ytsearch5:cats"));
            Assert.That(thirdPage.SkipWhile(argument => argument != "--playlist-start").Skip(1).First(), Is.EqualTo("41"));
            Assert.That(thirdPage.SkipWhile(argument => argument != "--playlist-end").Skip(1).First(), Is.EqualTo("60"));
        });
    }

    [Test]
    public void ALinkToNothingOrToAPlaylistIsNotOneItem()
    {
        Assert.Multiple(() =>
        {
            Assert.That(YtdlpService.ReadMedia("null"), Is.Null);
            Assert.That(YtdlpService.ReadCreator("""{"_type": "playlist", "channel": "Lofi Girl", "title": "Lofi Girl - Videos"}"""), Is.EqualTo("Lofi Girl"));
            Assert.That(YtdlpService.ReadMedia("""{"_type": "playlist", "id": "list", "title": "A list"}"""), Is.Null);
            Assert.That(YtdlpService.ReadMedia("""{"id": "abc", "title": "A video"}""")?.Title, Is.EqualTo("A video"));
        });
    }

    [Test]
    public void ASoundCloudChartIsReadIntoResults()
    {
        var json = """
            {"collection": [
                {"track": {"id": 42, "title": "Song", "permalink_url": "https://soundcloud.com/artist/song", "duration": 185000,
                           "playback_count": 1200, "artwork_url": "https://i1.sndcdn.com/artworks-abc-large.jpg",
                           "display_date": "2026-01-02T00:00:00Z", "user": {"username": "Artist", "permalink_url": "https://soundcloud.com/artist"}}},
                {"score": 1}
            ]}
            """;

        var tracks = MediaExport.Core.Media.Providers.SoundCloudCharts.ReadTracks(json);

        Assert.Multiple(() =>
        {
            Assert.That(tracks, Has.Count.EqualTo(1), "an entry without a track is left out");
            Assert.That(tracks[0].Identity, Is.EqualTo("42"));
            Assert.That(tracks[0].Duration, Is.EqualTo(TimeSpan.FromSeconds(185)));
            Assert.That(tracks[0].Views, Is.EqualTo(1200));
            Assert.That(tracks[0].ThumbnailUri?.AbsoluteUri, Does.EndWith("-t500x500.jpg"));
            Assert.That(tracks[0].CreatorUri?.AbsoluteUri, Is.EqualTo("https://soundcloud.com/artist"));
        });
    }
}
