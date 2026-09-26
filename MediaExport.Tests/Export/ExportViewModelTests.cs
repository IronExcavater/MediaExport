// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Activity;
using MediaExport.Core.Exporting;
using MediaExport.Core.Media.Models;
using MediaExport.Core.Settings;
using MediaExport.Export;

namespace MediaExport.Tests.Export;

public sealed class ExportViewModelTests
{
    private static readonly MediaSearchResult Video = new("id", "https://example.test/media", null, "Result", "Creator", TimeSpan.FromMinutes(10), ProviderId: "youtube");
    private static readonly MediaSearchResult Track = Video with { SourceUrl = "https://example.test/track", Kind = MediaKind.Audio, ProviderId = "soundcloud" };

    private static readonly MediaDetails Details = new(
        null,
        [new VideoQuality(1080, 250L << 20), new VideoQuality(720, 120L << 20)],
        10L << 20,
        [new SubtitleTrack("en", "English", false)],
        [new Chapter(TimeSpan.Zero, TimeSpan.FromMinutes(1), "Intro")],
        true);

    [Test]
    public async Task Choosing_media_checks_what_it_has_then_offers_just_that()
    {
        var downloader = new FakeDownloader();
        ExportViewModel viewModel = Create(downloader);

        viewModel.Open(Video);
        ExportState whileChecking = viewModel.State;
        downloader.Inspection.SetResult(Details);
        await Task.Yield();

        Assert.Multiple(() =>
        {
            Assert.That(whileChecking, Is.EqualTo(ExportState.Checking));
            Assert.That(viewModel.State, Is.EqualTo(ExportState.Ready));
            Assert.That(viewModel.VideoQuality.Items.Select(choice => choice.Label), Is.EqualTo(new[] { "Best", "1080p", "720p" }));
            Assert.That(viewModel.VideoQuality.SelectedKey, Is.EqualTo("1080"), "the remembered height");
            Assert.That(viewModel.StatusText, Is.EqualTo("About 250 MB · saves to Downloads"));
            Assert.That(viewModel.HasSubtitles && viewModel.HasChapters && viewModel.CanRemoveSponsors, Is.True);
        });
    }

    [Test]
    public void Media_that_cant_be_exported_says_why()
    {
        var downloader = new FakeDownloader();
        downloader.Inspection.SetResult(MediaDetails.Unavailable("It's protected by DRM, so it can't be exported."));
        ExportViewModel viewModel = Create(downloader);

        viewModel.Open(Track);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.State, Is.EqualTo(ExportState.Blocked));
            Assert.That(viewModel.StatusText, Is.EqualTo("It's protected by DRM, so it can't be exported."));
            Assert.That(viewModel.CanStart, Is.False);
        });
    }

    [Test]
    public async Task The_button_follows_an_export_until_a_choice_changes()
    {
        var downloader = new FakeDownloader();
        downloader.Inspection.SetResult(Details);
        ExportViewModel viewModel = Create(downloader);
        viewModel.Open(Video);

        viewModel.ExportCommand.Execute(null);
        ExportState running = viewModel.State;
        downloader.Download.SetResult([@"C:\out\Result.mp4"]);
        await Task.Yield();
        ExportState done = viewModel.State;
        string? saved = viewModel.StatusText;
        viewModel.VideoQuality.Select("720");

        Assert.Multiple(() =>
        {
            Assert.That(running, Is.EqualTo(ExportState.Running));
            Assert.That(done, Is.EqualTo(ExportState.Done));
            Assert.That(saved, Is.EqualTo("Saved as Result.mp4"));
            Assert.That(viewModel.State, Is.EqualTo(ExportState.Ready));
        });
    }

    [Test]
    public async Task A_failed_export_offers_to_try_again_and_says_what_went_wrong()
    {
        var downloader = new FakeDownloader();
        downloader.Inspection.SetResult(Details);
        ExportViewModel viewModel = Create(downloader);
        viewModel.Open(Video);

        viewModel.ExportCommand.Execute(null);
        downloader.Download.SetException(new InvalidOperationException("Disk full"));
        await Task.Yield();

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.State, Is.EqualTo(ExportState.Failed));
            Assert.That(viewModel.ExportLabel, Is.EqualTo("Try again"));
            Assert.That(viewModel.StatusText, Is.EqualTo("Disk full"));
            Assert.That(viewModel.CanStart, Is.True);
        });
    }

    [Test]
    public void Clips_and_extras_go_into_the_export_options()
    {
        var downloader = new FakeDownloader();
        downloader.Inspection.SetResult(Details);
        ExportViewModel viewModel = Create(downloader);
        viewModel.Open(Video);

        viewModel.Clips.Add(new Clip(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20)));
        viewModel.Clips.Add(new Clip(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(50)));
        viewModel.Subtitles[0].IsSelected = true;
        viewModel.RemoveSponsors = true;
        ExportOptions options = viewModel.CreateOptions();

        Assert.Multiple(() =>
        {
            Assert.That(options.Clips, Has.Count.EqualTo(2));
            Assert.That(options.SubtitleLanguages, Is.EqualTo(new[] { "en" }));
            Assert.That(options.RemoveSponsors, Is.True);
            Assert.That(options.EmbedChapters, Is.False, "chapters don't line up with clips");
            Assert.That(viewModel.ExportLabel, Is.EqualTo("Export 2 clips as one"));
            Assert.That(viewModel.ClipsSummary, Is.EqualTo("2 clips, 0:30 in all"));
        });
    }

    [Test]
    public void Audio_media_exports_audio_and_choices_are_remembered()
    {
        var downloader = new FakeDownloader();
        downloader.Inspection.SetResult(new MediaDetails(null, [], 5L << 20, [], [], false));
        var settings = new SettingsStore(null);
        ExportViewModel viewModel = Create(downloader, settings);

        viewModel.Open(Track);
        viewModel.AudioFormat.Select("flac");

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsAudio, Is.True);
            Assert.That(viewModel.Kind.Items[0].IsEnabled, Is.False, "there's no video to export");
            Assert.That(settings.Current.AudioFormat, Is.EqualTo("flac"));
            Assert.That(settings.Current.IsAudioExport, Is.True);
        });
    }

    [Test]
    public void Restoring_remembered_choices_keeps_every_one_of_them()
    {
        var settings = new SettingsStore(null);
        settings.Current.EmbedMetadata = settings.Current.EmbedThumbnail = settings.Current.EmbedChapters = true;
        settings.Current.RemoveSponsors = true;

        ExportViewModel viewModel = Create(new FakeDownloader(), settings);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.EmbedDetails && viewModel.EmbedChapters && viewModel.RemoveSponsors, Is.True);
            Assert.That(settings.Current.EmbedMetadata && settings.Current.EmbedChapters, Is.True);
        });
    }

    [Test]
    public void Choice_groups_keep_exactly_one_picked()
    {
        var group = new ChoiceGroup([new Choice("a", "A"), new Choice("b", "B")], "a");

        group.Items[1].IsSelected = true;

        Assert.Multiple(() =>
        {
            Assert.That(group.SelectedKey, Is.EqualTo("b"));
            Assert.That(group.Items[0].IsSelected, Is.False);
        });
    }

    private static ExportViewModel Create(FakeDownloader downloader, SettingsStore? settings = null)
    {
        settings ??= new SettingsStore(null);
        return new ExportViewModel(downloader, new ActivityViewModel(downloader, settings), settings);
    }

    internal sealed class FakeDownloader : IMediaDownloader
    {
        public TaskCompletionSource<MediaDetails> Inspection { get; } = new();
        public TaskCompletionSource<IReadOnlyList<string>> Download { get; } = new();

        public Task<MediaDetails> InspectAsync(string sourceUrl, CancellationToken cancellationToken = default) => Inspection.Task;

        public Task<IReadOnlyList<string>> DownloadAsync(string sourceUrl, ExportOptions options, IProgress<ExportProgress>? progress = null, CancellationToken cancellationToken = default) =>
            Download.Task.WaitAsync(cancellationToken);
    }
}
