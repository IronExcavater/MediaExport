// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Activity;
using MediaExport.Core.Exporting;
using MediaExport.Core.Media.Models;
using MediaExport.Core.Settings;

namespace MediaExport.Tests.Activity;

public class ActivityViewModelTests
{
    private static readonly MediaSearchResult Media = new("id", "https://example.com/id", null, "Title", null, null);
    private static readonly ExportOptions Options = new("mp4", "folder");

    [Test]
    public void Start_WhenQueueExceedsLimit_StartsNextJobAfterCompletion()
    {
        // Arrange
        var downloader = new HeldDownloader();
        var activity = new ActivityViewModel(downloader, new SettingsStore(null));

        // Act
        var first = activity.Start(Media, Options);
        activity.Start(Media, Options);
        var third = activity.Start(Media, Options);
        downloader.Downloads[0].SetResult([]);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(first.State, Is.EqualTo(ExportJobState.Completed));
            Assert.That(third.State, Is.EqualTo(ExportJobState.Running));
            Assert.That(downloader.Downloads, Has.Count.EqualTo(3));
            Assert.That(activity.ActiveCount, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task RemoveSelected_WhenJobIsRunning_CancelsWorkAndClearsSelection()
    {
        // Arrange
        var downloader = new CancelAwareDownloader();
        var activity = new ActivityViewModel(downloader, new SettingsStore(null));
        var job = activity.Start(Media, Options);
        await downloader.Started.Task;
        activity.Select([job]);

        // Act
        activity.RemoveSelectedCommand.Execute(null);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(downloader.Token.IsCancellationRequested, Is.True);
            Assert.That(activity.Jobs, Is.Empty);
            Assert.That(activity.SelectedCount, Is.Zero);
        });
    }

    [Test]
    public void Report_WhenDownloadStageChanges_UsesCurrentProgressAndPhase()
    {
        // Arrange
        var downloader = new SequenceDownloader(
            [new ExportProgress(100, "3 MiB/s", "00:00"), new ExportProgress(24, "1 MiB/s", "00:05")],
            [new ExportProgress(100, "3 MiB/s", "00:00"), new ExportProgress(null, null, null, ExportPhase.Merging)]);
        var activity = new ActivityViewModel(downloader, new SettingsStore(null));

        // Act
        var downloading = activity.Start(Media, Options);
        var merging = activity.Start(Media, Options);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(downloading.Progress, Is.EqualTo(24));
            Assert.That(downloading.Status, Is.EqualTo("Downloading"));
            Assert.That(downloading.ProgressDetail, Is.EqualTo("24% · 1 MiB/s · 00:05 left"));
            Assert.That(merging.Status, Is.EqualTo("Merging"));
            Assert.That(activity.IsActiveProgressUnknown, Is.True);
            Assert.That(activity.ActiveProgress, Is.Zero);
        });
    }

    [Test]
    public async Task Retry_WhenFailureChangesToDrm_ReusesRowAndPersistsTerminalState()
    {
        // Arrange
        var settings = new SettingsStore(null);
        var downloader = new HeldDownloader();
        var activity = new ActivityViewModel(downloader, settings);
        var job = activity.Start(Media, Options);
        downloader.Downloads[0].SetException(new InvalidOperationException("Temporary failure"));

        // Act
        activity.Retry(job);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        job.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ExportJob.State) && job.IsFinished)
                finished.TrySetResult();
        };
        downloader.Downloads[1].SetException(new MediaExportException(ExportFailureKind.DrmProtected, "Protected"));
        await finished.Task;
        var restored = new ActivityViewModel(downloader, settings).Jobs.Single();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(activity.Jobs[0], Is.SameAs(job));
            Assert.That(job.State, Is.EqualTo(ExportJobState.Failed));
            Assert.That(job.CanRetry, Is.False);
            Assert.That(settings.Current.History.Single().FailureKind, Is.EqualTo(ExportFailureKind.DrmProtected));
            Assert.That(restored.CanRetry, Is.False);
        });
    }

    private sealed class HeldDownloader : IMediaDownloader
    {
        public List<TaskCompletionSource<IReadOnlyList<string>>> Downloads { get; } = [];

        public Task<IReadOnlyList<string>> DownloadAsync(
            string sourceUrl,
            ExportOptions options,
            IProgress<ExportProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var download = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
            Downloads.Add(download);
            return download.Task;
        }
    }

    private sealed class CancelAwareDownloader : IMediaDownloader
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token { get; private set; }

        public async Task<IReadOnlyList<string>> DownloadAsync(
            string sourceUrl,
            ExportOptions options,
            IProgress<ExportProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Token = cancellationToken;
            Started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return [];
        }
    }

    private sealed class SequenceDownloader(params ExportProgress[][] reports) : IMediaDownloader
    {
        private int _next;

        public Task<IReadOnlyList<string>> DownloadAsync(
            string sourceUrl,
            ExportOptions options,
            IProgress<ExportProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            foreach (var report in reports[_next++])
                progress!.Report(report);
            return new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously).Task;
        }
    }
}
