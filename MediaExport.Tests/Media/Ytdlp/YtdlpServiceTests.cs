// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Exporting;
using MediaExport.Core.Media.Providers;
using MediaExport.Core.Media.Ytdlp;
using MediaExport.Core.Settings;
using MediaExport.Core.Tooling;
using MediaExport.Tests.Support;

namespace MediaExport.Tests.Media.Ytdlp;

public class YtdlpServiceTests
{
    [Test]
    public void ReadProgress_WhenLineIsMarked_ReturnsPhaseSpeedAndEta()
    {
        // Arrange
        const string downloading = "ME_PROGRESS|downloading| 42.5%|1.2MiB/s|00:10";
        const string merging = "ME_PROGRESS|merging|||";

        // Act
        var progress = YtdlpService.ReadProgress(downloading);
        var merge = YtdlpService.ReadProgress(merging);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(progress, Is.EqualTo(new ExportProgress(42.5, "1.2MiB/s", "00:10", ExportPhase.Downloading)));
            Assert.That(merge, Is.EqualTo(new ExportProgress(null, null, null, ExportPhase.Merging)));
            Assert.That(YtdlpService.ReadProgress("not progress"), Is.Null);
        });
    }

    [Test]
    public void GetDownloadArguments_WhenFormatsDiffer_SelectsCompatibleStreamsWithoutReencoding()
    {
        // Arrange
        var videoOptions = new ExportOptions("mp4", @"C:\Exports", VideoHeight: 720);
        var audioOptions = new ExportOptions("mp3", @"C:\Exports", AudioBitrateKbps: 320);

        // Act
        var video = YtdlpService.GetDownloadArguments("https://example.test/video", videoOptions, @"C:\FFmpeg");
        var audio = YtdlpService.GetDownloadArguments("https://example.test/audio", audioOptions, @"C:\FFmpeg");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(ValueAfter(video, "--format"), Does.Contain("[ext=mp4][height<=720]"));
            Assert.That(ValueAfter(video, "--merge-output-format"), Is.EqualTo("mp4"));
            Assert.That(video, Does.Not.Contain("--recode-video"));
            Assert.That(ValueAfter(audio, "--audio-format"), Is.EqualTo("mp3"));
            Assert.That(ValueAfter(audio, "--audio-quality"), Is.EqualTo("320K"));
            Assert.That(video, Does.Contain("postprocess:ME_PROGRESS|merging|||"));
        });
    }

    [Test]
    public void Download_WhenDependenciesAreMissing_FailsBeforeStarting()
    {
        // Arrange
        using var temporary = new TemporaryDirectory();
        var toolsFolder = temporary.CreateDirectory("tools");
        var pathFolder = temporary.CreateDirectory("path");
        var service = CreateService(toolsFolder, pathFolder);
        var options = new ExportOptions("mp4", temporary.Path);

        // Act
        var missingYtDlp = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.DownloadAsync("https://example.test/video", options));
        temporary.CreateFile(pathFolder, "yt-dlp.exe");
        var missingFfmpeg = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.DownloadAsync("https://example.test/video", options));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(missingYtDlp!.Message, Does.Contain("yt-dlp isn't installed"));
            Assert.That(missingFfmpeg!.Message, Does.Contain("FFmpeg isn't installed"));
        });
    }

    [Test]
    public void ParseResults_WhenDataContainsMediaAndFailures_ReturnsUsableDomainValues()
    {
        // Arrange
        const string json = """
            {"entries":[
              {"id":"channel","title":"Channel","ie_key":"YoutubeTab"},
              {"id":"abc","title":"A song","url":"https://youtube.test/watch?v=abc","channel":"Artist","duration":187,
               "view_count":1900000,"timestamp":1700000000,"thumbnails":[{"url":"https://img/small.jpg"},{"url":"https://img/large.jpg"}]}
            ]}
            """;
        const string protectedJson = """{"formats":[{"has_drm":true},{"has_drm":true}]}""";

        // Act
        var result = YtdlpService.ReadSearchResults(json).Single();
        var availability = YtdlpService.ReadAvailability(protectedJson);
        var drm = YtdlpService.NormalizeError("This video is DRM protected");
        var forbidden = YtdlpService.NormalizeError("HTTP Error 403: Forbidden");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Identity, Is.EqualTo("abc"));
            Assert.That(result.Duration, Is.EqualTo(TimeSpan.FromSeconds(187)));
            Assert.That(result.Views, Is.EqualTo(1_900_000));
            Assert.That(result.ThumbnailUri, Is.EqualTo(new Uri("https://img/large.jpg")));
            Assert.That(availability.CanExport, Is.False);
            Assert.That(((MediaExportException)drm).Kind, Is.EqualTo(ExportFailureKind.DrmProtected));
            Assert.That(forbidden.Message, Does.Contain("update yt-dlp"));
            Assert.That(YtdlpService.ReadMedia("null"), Is.Null);
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
