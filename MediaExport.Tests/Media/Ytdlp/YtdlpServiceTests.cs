// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Exporting;
using MediaExport.Core.Media.Ytdlp;
using MediaExport.Core.Settings;
using MediaExport.Core.Tooling;
using MediaExport.Tests.Support;

namespace MediaExport.Tests.Media.Ytdlp;

public class YtdlpServiceTests
{
    [Test]
    public void Marked_progress_includes_phase_speed_and_eta_and_ignores_noise()
    {
        var progress = YtdlpService.ReadProgress("ME_PROGRESS|downloading| 42.5%|1.2MiB/s|00:10");
        var merging = YtdlpService.ReadProgress("ME_PROGRESS|merging|||");
        var converting = YtdlpService.ReadProgress("ME_PROGRESS|converting|||");

        Assert.Multiple(() =>
        {
            Assert.That(progress, Is.EqualTo(new ExportProgress(42.5, "1.2MiB/s", "00:10", ExportPhase.Downloading)));
            Assert.That(merging, Is.EqualTo(new ExportProgress(null, null, null, ExportPhase.Merging)));
            Assert.That(converting, Is.EqualTo(new ExportProgress(null, null, null, ExportPhase.Converting)));
            Assert.That(YtdlpService.ReadProgress("not progress"), Is.Null);
        });
    }

    [Test]
    public void Availability_is_blocked_only_when_every_format_is_drm_protected()
    {
        var blocked = YtdlpService.ReadAvailability("""{"formats":[{"format_id":"one","has_drm":true},{"format_id":"two","has_drm":true}]}""");
        var available = YtdlpService.ReadAvailability("""{"formats":[{"format_id":"one","has_drm":true},{"format_id":"two","has_drm":false}]}""");

        Assert.Multiple(() =>
        {
            Assert.That(blocked.CanExport, Is.False);
            Assert.That(blocked.FailureKind, Is.EqualTo(ExportFailureKind.DrmProtected));
            Assert.That(available.CanExport, Is.True);
        });
    }

    [Test]
    public void Drm_console_errors_become_a_typed_export_failure()
    {
        var failure = YtdlpService.NormalizeError("This video is DRM protected");

        Assert.That(failure, Is.TypeOf<MediaExportException>());
        Assert.That(((MediaExportException)failure).Kind, Is.EqualTo(ExportFailureKind.DrmProtected));
    }

    [Test]
    public void Download_requires_yt_dlp_and_ffmpeg_before_starting()
    {
        using var temporary = new TemporaryDirectory();
        var toolsFolder = temporary.CreateDirectory("tools");
        var pathFolder = temporary.CreateDirectory("path");
        var service = CreateService(toolsFolder, pathFolder);
        var options = new ExportOptions("mp4", temporary.Path);

        var missingYtDlp = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.DownloadAsync("https://example.test/video", options));
        temporary.CreateFile(pathFolder, "yt-dlp.exe");
        var missingFfmpeg = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.DownloadAsync("https://example.test/video", options));

        Assert.Multiple(() =>
        {
            Assert.That(missingYtDlp!.Message, Does.Contain("yt-dlp isn't installed"));
            Assert.That(missingFfmpeg!.Message, Does.Contain("FFmpeg isn't installed"));
        });
    }

    [Test]
    public void Download_arguments_match_the_requested_media_without_reencoding()
    {
        var video = YtdlpService.GetDownloadArguments(
            "https://example.test/video",
            new ExportOptions("mp4", @"C:\Exports", VideoHeight: 720),
            @"C:\FFmpeg");
        var audio = YtdlpService.GetDownloadArguments(
            "https://example.test/audio",
            new ExportOptions("mp3", @"C:\Exports", AudioBitrateKbps: 320),
            @"C:\FFmpeg");
        var webm = YtdlpService.GetDownloadArguments(
            "https://example.test/video",
            new ExportOptions("webm", @"C:\Exports"),
            @"C:\FFmpeg");
        var mkv = YtdlpService.GetDownloadArguments(
            "https://example.test/video",
            new ExportOptions("mkv", @"C:\Exports"),
            @"C:\FFmpeg");

        Assert.Multiple(() =>
        {
            Assert.That(ValueAfter(video, "--format"), Does.Contain("[ext=mp4][height<=720]"));
            Assert.That(ValueAfter(video, "--merge-output-format"), Is.EqualTo("mp4"));
            Assert.That(ValueAfter(video, "--remux-video"), Is.EqualTo("mp4"));
            Assert.That(video, Does.Not.Contain("--recode-video"));
            Assert.That(ValueAfter(webm, "--format"), Does.Contain("[ext=webm]"));
            Assert.That(ValueAfter(mkv, "--format"), Does.Not.Contain("[ext="));
            Assert.That(ValueAfter(audio, "--audio-format"), Is.EqualTo("mp3"));
            Assert.That(ValueAfter(audio, "--audio-quality"), Is.EqualTo("320K"));
            Assert.That(ValueAfter(video, "--ffmpeg-location"), Is.EqualTo(@"C:\FFmpeg"));
            Assert.That(video, Does.Contain("download:ME_PROGRESS|downloading|%(progress._percent_str)s|%(progress._speed_str)s|%(progress._eta_str)s"));
            Assert.That(video, Does.Contain("postprocess:ME_PROGRESS|merging|||"));
            Assert.That(video, Does.Contain("after_move:ME_FILE|%(filepath)s"));
            Assert.That(video.Last(), Is.EqualTo("https://example.test/video"));
        });
    }

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

    private static YtdlpService CreateService(string toolsFolder, string pathFolder)
    {
        var toolchain = new ToolchainService(
            new SettingsStore(null),
            new ToolchainOptions(toolsFolder, [pathFolder]),
            new HttpClient(),
            new FakeProcessRunner());
        return new YtdlpService(toolchain);
    }

    private static string ValueAfter(IReadOnlyList<string> arguments, string option) =>
        arguments[arguments.ToList().IndexOf(option) + 1];
}
