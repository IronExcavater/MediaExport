// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Exporting;
using MediaExport.Core.Media.Models;
using MediaExport.Core.Media.Ytdlp;
using System.Diagnostics;

namespace MediaExport.Tests.Media;

public sealed class YtdlpServiceTests
{
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
    public void ReadSearchResults_prefers_the_channel_url_and_ignores_empty_values()
    {
        const string json = """
            {"entries":[{"id":"abc123","title":"Result","uploader_url":"","channel_url":"https://example.test/channel"}]}
            """;

        IReadOnlyList<YtdlpSearchResult> results = YtdlpService.ReadSearchResults(json);

        Assert.That(results[0].CreatorUrl, Is.EqualTo("https://example.test/channel"));
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
    public void SearchAsync_preserves_a_cancelled_search()
    {
        var service = new YtdlpService();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Stopwatch stopwatch = Stopwatch.StartNew();

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await service.SearchAsync("ytsearch1:gan", cancellation.Token));

        Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(1)));
    }
}
