// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Exporting;
using MediaExport.Core.Media.Models;
using MediaExport.Core.Media.Ytdlp;
using MediaExport.Core.Tooling;
using System.Diagnostics;
using System.Text.Json;

namespace MediaExport.Tests.Media;

public sealed class YtdlpServiceTests
{
    internal static YtdlpService CreateService() =>
        new(new ToolchainService(Path.Combine(Path.GetTempPath(), "mediaexport-no-tools")));

    [Test]
    public void ReadSearchResults_maps_a_ytdlp_search_response()
    {
        const string json = """
            {"entries":[{"id":"abc123","webpage_url":"https://example.test/media","title":"Result","uploader":"Channel","duration":125,"thumbnails":[{"url":"https://example.test/small.jpg"},{"url":"https://example.test/large.jpg"}]}]}
            """;

        IReadOnlyList<YtdlpSearchResult> results = YtdlpService.ReadSearchResults(json);

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(results[0].SourceUrl, Is.EqualTo("https://example.test/media"));
            Assert.That(results[0].Creator, Is.EqualTo("Channel"));
            Assert.That(results[0].Duration, Is.EqualTo(TimeSpan.FromSeconds(125)));
            Assert.That(results[0].ThumbnailUri, Is.EqualTo(new Uri("https://example.test/large.jpg")));
        });
    }

    [Test]
    public void ReadDetails_offers_only_what_the_media_really_has()
    {
        const string json = """
            {
              "extractor_key": "Youtube",
              "formats": [
                {"format_id": "sb0", "vcodec": "none", "acodec": "none", "height": 90},
                {"format_id": "140", "vcodec": "none", "acodec": "mp4a.40.2", "filesize": 10000000},
                {"format_id": "299", "vcodec": "avc1.64002a", "acodec": "none", "height": 1080, "filesize": 250000000},
                {"format_id": "303", "vcodec": "vp9", "acodec": "none", "height": 1080, "filesize": 160000000},
                {"format_id": "298", "vcodec": "avc1.4d4020", "acodec": "none", "height": 720, "filesize_approx": 150000000.0}
              ],
              "subtitles": {"en": [{"ext": "vtt", "name": "English"}], "live_chat": [{"ext": "json"}]},
              "automatic_captions": {"de": [{"name": "German"}], "en-orig": [{"name": "English (Original)"}]},
              "chapters": [{"start_time": 0, "end_time": 60, "title": "Intro"}, {"start_time": 60, "end_time": 300, "title": "Main"}]
            }
            """;

        using JsonDocument document = JsonDocument.Parse(json);
        MediaDetails details = YtdlpService.ReadDetails(document.RootElement);

        Assert.Multiple(() =>
        {
            Assert.That(details.IsExportable, Is.True);
            Assert.That(details.VideoQualities.Select(quality => quality.Height), Is.EqualTo(new[] { 1080, 720 }));
            Assert.That(details.VideoQualities[0].ApproximateBytes, Is.EqualTo(260_000_000), "the H.264 stream plus the audio");
            Assert.That(details.AudioBytes, Is.EqualTo(10_000_000));
            Assert.That(details.Subtitles.Select(track => track.Name), Is.EqualTo(new[] { "English", "English (automatic)" }));
            Assert.That(details.Subtitles[1].Language, Is.EqualTo("en-orig"));
            Assert.That(details.Chapters.Select(chapter => chapter.Title), Is.EqualTo(new[] { "Intro", "Main" }));
            Assert.That(details.SupportsSponsorBlock, Is.True);
        });
    }

    [Test]
    public void ReadDetails_explains_media_that_is_all_DRM()
    {
        using JsonDocument document = JsonDocument.Parse("""{"formats":[{"format_id":"hls","acodec":"opus","has_drm":true}]}""");

        MediaDetails details = YtdlpService.ReadDetails(document.RootElement);

        Assert.That(details.Problem, Is.EqualTo("It's protected by DRM, so it can't be exported."));
    }

    [Test]
    public void Audio_exports_extract_and_convert_the_best_audio()
    {
        var arguments = YtdlpService.GetExportArguments(new ExportOptions(MediaKind.Audio, "mp3", null, "out"));

        Assert.That(arguments, Is.SupersetOf(new (string, string?)[]
        {
            ("-f", "ba/b"), ("-x", null), ("--audio-format", "mp3"), ("--embed-thumbnail", null)
        }));
    }

    [Test]
    public void Video_exports_prefer_the_chosen_height_and_compatible_codecs()
    {
        var arguments = YtdlpService.GetExportArguments(new ExportOptions(MediaKind.Video, "mp4", 1080, "out"));

        Assert.That(arguments, Is.SupersetOf(new (string, string?)[]
        {
            ("-f", "bv*+ba/b"), ("-S", "res:1080,vcodec:h264,acodec:aac"), ("--merge-output-format", "mp4")
        }));
    }

    [Test]
    public void Clips_subtitles_sponsors_and_sign_in_become_arguments()
    {
        var options = new ExportOptions(MediaKind.Video, "mkv", null, "out")
        {
            Clips = [new Clip(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(45.5)), new Clip(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(3))],
            SubtitleLanguages = ["en", "de-orig"],
            RemoveSponsors = true,
            CookiesFromBrowser = "edge",
            EmbedChapters = false
        };

        var arguments = YtdlpService.GetExportArguments(options);

        Assert.Multiple(() =>
        {
            Assert.That(arguments, Is.SupersetOf(new (string, string?)[]
            {
                ("--download-sections", "*30-45.5"), ("--download-sections", "*120-180"), ("--force-keyframes-at-cuts", null),
                ("--sub-langs", "en,de-orig"), ("--write-auto-subs", null), ("--embed-subs", null),
                ("--sponsorblock-remove", "sponsor,selfpromo,interaction"), ("--cookies-from-browser", "edge")
            }));
            Assert.That(arguments, Does.Not.Contain(("--embed-chapters", (string?)null)));
            Assert.That(YtdlpService.GetOutputTemplate(options), Is.EqualTo("%(title)s (part %(section_number)s).%(ext)s"));
        });
    }

    [Test]
    public void Audio_bitrate_is_passed_as_a_quality()
    {
        var arguments = YtdlpService.GetExportArguments(new ExportOptions(MediaKind.Audio, "mp3", null, "out") { AudioBitrate = 192 });

        Assert.That(arguments, Does.Contain(("--audio-quality", (string?)"192K")));
    }

    [TestCase("ERROR: [soundcloud] 257461521: This video is DRM protected", "It's protected by DRM, so it can't be exported.")]
    [TestCase("ERROR: [youtube] xxxxxxxxxxx: This video is unavailable", "This video is unavailable")]
    [TestCase("ERROR: Unable to download webpage: timed out", "Unable to download webpage: timed out")]
    public void DescribeError_strips_ytdlp_prefixes(string message, string expected) =>
        Assert.That(YtdlpService.DescribeError(message), Is.EqualTo(expected));

    [Test]
    public void SearchAsync_preserves_a_cancelled_search()
    {
        YtdlpService service = CreateService();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Stopwatch stopwatch = Stopwatch.StartNew();

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await service.SearchAsync("ytsearch1:gan", cancellation.Token));

        Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(1)));
    }
}
