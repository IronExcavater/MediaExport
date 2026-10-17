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
    public async Task OpenLink_WhenProviderFails_ReturnsProblemAndClearsLoadingState()
    {
        // Arrange
        var provider = new FakeProvider { Failure = new InvalidOperationException("Offline") };
        var export = CreateViewModel([provider]);

        // Act
        var problem = await export.OpenLinkAsync(new Uri("https://example.test/id"));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(problem, Is.EqualTo("Couldn't open that link. Offline"));
            Assert.That(export.Media, Is.Null);
            Assert.That(export.IsLoading, Is.False);
        });
    }

    [Test]
    public void Export_WhenOptionsAreSelected_QueuesValuesAndPersistsFolder()
    {
        // Arrange
        using var temporary = new TemporaryDirectory();
        var settingsPath = Path.Combine(temporary.Path, "settings.json");
        var outputFolder = temporary.CreateDirectory("exports");
        var settings = new SettingsStore(settingsPath);
        var downloader = new CapturingDownloader();
        var export = CreateViewModel([], settings, downloader);
        export.Open(Media);
        export.SelectedOutputFolder = outputFolder;
        export.SelectedVideoResolution = "720p";
        export.SelectedFormat = "webm";

        // Act
        export.ExportCommand.Execute(null);
        var restored = CreateViewModel([], new SettingsStore(settingsPath));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(downloader.Options.Single(), Is.EqualTo(new ExportOptions("webm", outputFolder, 192, 720)));
            Assert.That(restored.SelectedOutputFolder, Is.EqualTo(outputFolder));
        });
    }

    [Test]
    public async Task Export_WhenJobReportsProgress_UpdatesButtonUntilCompletion()
    {
        // Arrange
        var downloader = new ProgressDownloader();
        var export = CreateViewModel([], downloader: downloader);
        export.Open(Media);

        // Act
        export.ExportCommand.Execute(null);
        await downloader.Started.Task;
        var activeText = export.ExportActionText;
        var activeDetail = export.ExportProgressDetail;
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        export.CurrentJob!.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ExportJob.State) && export.CurrentJob.IsFinished)
                finished.TrySetResult();
        };
        downloader.Download.SetResult([]);
        await finished.Task;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(activeText, Is.EqualTo("Downloading"));
            Assert.That(activeDetail, Is.EqualTo("42% · 2 MiB/s · 00:12 left"));
            Assert.That(export.ExportProgress, Is.EqualTo(100));
            Assert.That(export.ExportActionText, Is.EqualTo("Export again"));
            Assert.That(export.ExportCommand.CanExecute(null), Is.True);
        });
    }

    private static ExportViewModel CreateViewModel(
        IEnumerable<IMediaSearchProvider> providers,
        SettingsStore? settings = null,
        IMediaDownloader? downloader = null)
    {
        settings ??= new SettingsStore(null);
        downloader ??= new UnusedDownloader();
        var activity = new ActivityViewModel(downloader, settings);
        return new ExportViewModel(activity, settings, providers, downloader);
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

    private sealed class ProgressDownloader : IMediaDownloader
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<IReadOnlyList<string>> Download { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<string>> DownloadAsync(
            string sourceUrl,
            ExportOptions options,
            IProgress<ExportProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            progress!.Report(new ExportProgress(42, "2 MiB/s", "00:12"));
            Started.SetResult();
            return Download.Task;
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
        public LinkKind GetLinkKind(Uri link) => LinkKind.Item;
        public Task<MediaSearchResult?> FindByLinkAsync(Uri link, CancellationToken cancellationToken = default) =>
            Failure is null ? Task.FromResult<MediaSearchResult?>(Media) : Task.FromException<MediaSearchResult?>(Failure);
        public Task<IReadOnlyList<MediaSearchResult>> SearchAsync(string query, int skip = 0, int limit = 10, SearchSort sort = SearchSort.Relevance, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MediaSearchResult>>([]);
        public Task<IReadOnlyList<MediaSearchResult>> ListByLinkAsync(Uri link, int skip = 0, int limit = 10, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MediaSearchResult>>([]);
        public Task<string?> FindCreatorAsync(Uri link, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }
}
