// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Activity;
using MediaExport.Core.Exporting;
using MediaExport.Core.Media.Models;
using MediaExport.Core.Media.Providers;
using MediaExport.Core.Settings;
using MediaExport.Export;
using MediaExport.Tests.Support;

namespace MediaExport.Tests.Export;

public class ExportViewModelTests
{
    private static readonly MediaSearchResult Media = new(
        "id", "https://example.test/id", null, "Title", null, null);

    [Test]
    public void Close_clears_the_selected_media_but_keeps_it_recent()
    {
        var export = CreateViewModel([]);
        export.Open(Media);

        export.Close();

        Assert.Multiple(() =>
        {
            Assert.That(export.Media, Is.Null);
            Assert.That(export.IsOpen, Is.False);
            Assert.That(export.RecentlyViewed.Select(item => item.Media), Is.EqualTo(new[] { Media }));
        });
    }

    [Test]
    public async Task Close_cancels_an_inflight_link_and_ignores_its_late_result()
    {
        var provider = new FakeProvider { Pause = new TaskCompletionSource<MediaSearchResult?>(TaskCreationOptions.RunContinuationsAsynchronously) };
        var export = CreateViewModel([provider]);

        var opening = export.OpenLinkAsync(new Uri("https://example.test/id"));
        await provider.Started.Task;
        export.Close();
        provider.Pause.TrySetResult(Media);
        var problem = await opening;

        Assert.Multiple(() =>
        {
            Assert.That(provider.WasCancelled, Is.True);
            Assert.That(problem, Is.Null);
            Assert.That(export.Media, Is.Null);
            Assert.That(export.IsLoading, Is.False);
            Assert.That(export.RecentlyViewed, Is.Empty);
        });
    }

    [Test]
    public async Task Provider_failure_returns_a_problem_and_clears_loading()
    {
        var provider = new FakeProvider { Failure = new InvalidOperationException("Offline") };
        var export = CreateViewModel([provider]);

        var problem = await export.OpenLinkAsync(new Uri("https://example.test/id"));

        Assert.Multiple(() =>
        {
            Assert.That(problem, Is.EqualTo("Couldn't open that link. Offline"));
            Assert.That(export.Media, Is.Null);
            Assert.That(export.IsLoading, Is.False);
        });
    }

    [Test]
    public void Selected_folder_is_persisted_and_restored()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "settings.json");
        var folder = temporary.CreateDirectory("exports");
        var export = CreateViewModel([], new SettingsStore(path));

        export.SelectedOutputFolder = folder;
        var restored = CreateViewModel([], new SettingsStore(path));

        Assert.Multiple(() =>
        {
            Assert.That(new SettingsStore(path).Current.ExportFolder, Is.EqualTo(folder));
            Assert.That(restored.SelectedOutputFolder, Is.EqualTo(folder));
        });
    }

    [Test]
    public void Export_queues_the_selected_video_and_audio_options()
    {
        var downloader = new CapturingDownloader();
        var export = CreateViewModel([], downloader: downloader);
        export.Open(Media);
        export.SelectedVideoResolution = "720p";
        export.SelectedFormat = "webm";
        export.ExportCommand.Execute(null);
        var video = downloader.Options.Single();

        export.SelectedExportMode = "Audio only";
        export.SelectedFormat = "m4a";
        export.SelectedAudioBitrate = "320 kbps";
        export.ExportCommand.Execute(null);
        var audio = downloader.Options.Last();
        export.Open(Media with { Kind = MediaKind.Audio });

        Assert.Multiple(() =>
        {
            Assert.That(video, Is.EqualTo(new ExportOptions("webm", export.SelectedOutputFolder, 192, 720)));
            Assert.That(audio, Is.EqualTo(new ExportOptions("m4a", export.SelectedOutputFolder, 320, 0)));
            Assert.That(export.ExportModes, Is.EqualTo(new[] { "Audio only" }));
            Assert.That(export.SelectedExportMode, Is.EqualTo("Audio only"));
            Assert.That(export.SelectedFormat, Is.EqualTo("mp3"));
        });
    }

    private static ExportViewModel CreateViewModel(
        IEnumerable<IMediaSearchProvider> providers,
        SettingsStore? settings = null,
        IMediaDownloader? downloader = null)
    {
        settings ??= new SettingsStore(null);
        var activity = new ActivityViewModel(downloader ?? new UnusedDownloader(), settings);
        return new ExportViewModel(activity, settings, providers);
    }

    private sealed class CapturingDownloader : IMediaDownloader
    {
        public List<ExportOptions> Options { get; } = [];

        public Task<IReadOnlyList<string>> DownloadAsync(
            string sourceUrl,
            ExportOptions options,
            IProgress<ExportProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Options.Add(options);
            return Task.FromResult<IReadOnlyList<string>>([]);
        }
    }

    private sealed class UnusedDownloader : IMediaDownloader
    {
        public Task<IReadOnlyList<string>> DownloadAsync(
            string sourceUrl,
            ExportOptions options,
            IProgress<ExportProgress>? progress = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeProvider : IMediaSearchProvider
    {
        public string Id => "fake";
        public string DisplayName => "fake";
        public string Icon => "icon";
        public MediaKind Kind => MediaKind.Video;
        public Exception? Failure { get; init; }
        public TaskCompletionSource<MediaSearchResult?>? Pause { get; init; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool WasCancelled { get; private set; }

        public LinkKind GetLinkKind(Uri link) => LinkKind.Item;

        public async Task<MediaSearchResult?> FindByLinkAsync(
            Uri link,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            if (Failure is not null)
                throw Failure;
            if (Pause is null)
                return Media;

            try
            {
                return await Pause.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                WasCancelled = true;
                throw;
            }
        }

        public Task<IReadOnlyList<MediaSearchResult>> SearchAsync(
            string query,
            int skip = 0,
            int limit = 10,
            SearchSort sort = SearchSort.Relevance,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<MediaSearchResult>>([]);

        public Task<IReadOnlyList<MediaSearchResult>> ListByLinkAsync(
            Uri link,
            int skip = 0,
            int limit = 10,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<MediaSearchResult>>([]);

        public Task<string?> FindCreatorAsync(Uri link, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }
}
