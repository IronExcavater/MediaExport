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

    // Each download stays unfinished until the test finishes it.
    private sealed class HeldDownloader : IMediaDownloader
    {
        public List<TaskCompletionSource<IReadOnlyList<string>>> Downloads { get; } = [];

        public Task<IReadOnlyList<string>> DownloadAsync(
            string sourceUrl,
            ExportOptions options,
            IProgress<ExportProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var download = new TaskCompletionSource<IReadOnlyList<string>>();
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

    [Test]
    public void TwoExportsRunAtOnceAndTheNextStartsWhenOneFinishes()
    {
        var downloader = new HeldDownloader();
        var activity = new ActivityViewModel(downloader, new SettingsStore(null));

        var first = activity.Start(Media, Options);
        activity.Start(Media, Options);
        var third = activity.Start(Media, Options);

        Assert.That(downloader.Downloads, Has.Count.EqualTo(2));
        Assert.That(third.State, Is.EqualTo(ExportJobState.Queued));
        Assert.That(activity.ActiveCount, Is.EqualTo(3));

        downloader.Downloads[0].SetResult([]);

        Assert.That(first.State, Is.EqualTo(ExportJobState.Completed));
        Assert.That(third.State, Is.EqualTo(ExportJobState.Running));
        Assert.That(activity.ActiveCount, Is.EqualTo(2));
    }

    [Test]
    public void AQueuedExportThatIsCancelledNeverRuns()
    {
        var downloader = new HeldDownloader();
        var activity = new ActivityViewModel(downloader, new SettingsStore(null));
        activity.Start(Media, Options);
        activity.Start(Media, Options);
        var queued = activity.Start(Media, Options);

        queued.CancelCommand.Execute(null);
        downloader.Downloads[0].SetResult([]);

        Assert.That(queued.State, Is.EqualTo(ExportJobState.Cancelled));
        Assert.That(downloader.Downloads, Has.Count.EqualTo(2));
        Assert.That(activity.ActiveCount, Is.EqualTo(1));
    }

    [Test]
    public async Task Cancelling_a_running_export_reaches_the_downloader_and_is_saved_to_history()
    {
        var settings = new SettingsStore(null);
        var downloader = new CancelAwareDownloader();
        var activity = new ActivityViewModel(downloader, settings);
        var job = activity.Start(Media, Options);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        job.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ExportJob.State) && job.IsCancelled)
                cancelled.TrySetResult();
        };
        await downloader.Started.Task;

        job.CancelCommand.Execute(null);
        await cancelled.Task;

        Assert.Multiple(() =>
        {
            Assert.That(downloader.Token.IsCancellationRequested, Is.True);
            Assert.That(job.State, Is.EqualTo(ExportJobState.Cancelled));
            Assert.That(settings.Current.History, Has.Count.EqualTo(1));
            Assert.That(settings.Current.History[0].Outcome, Is.EqualTo(ExportOutcome.Cancelled));
        });
    }

    [Test]
    public void AFailedDownloadBecomesTheJobsStatus()
    {
        var downloader = new HeldDownloader();
        var activity = new ActivityViewModel(downloader, new SettingsStore(null));
        var job = activity.Start(Media, Options);

        downloader.Downloads[0].SetException(new InvalidOperationException("No connection."));

        Assert.That(job.State, Is.EqualTo(ExportJobState.Failed));
        Assert.That(job.Status, Is.EqualTo("No connection."));
        Assert.That(activity.HasActive, Is.False);
    }

    [Test]
    public void FinishedExportsAreKeptForTheNextRunAndListedByDay()
    {
        var settings = new SettingsStore(null);
        var downloader = new HeldDownloader();
        var activity = new ActivityViewModel(downloader, settings);
        activity.Start(Media, Options);
        activity.Start(Media, Options);
        downloader.Downloads[0].SetResult(["folder/file.mp4"]);

        var nextRun = new ActivityViewModel(downloader, settings);
        var headings = activity.Groups.Select(group => group.Title);

        Assert.That(nextRun.Jobs, Has.Count.EqualTo(1));
        Assert.That(nextRun.Jobs[0].IsCompleted, Is.True);
        Assert.That(headings, Is.EqualTo(new[] { "Exporting", "Today" }));
    }
}
