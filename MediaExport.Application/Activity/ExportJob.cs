// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaExport.Core.Exporting;
using MediaExport.Core.Media.Models;
using MediaExport.Core.Settings;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MediaExport.Activity;

public enum ExportJobState
{
    /// <summary>Waiting its turn behind the exports already running.</summary>
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled
}

/// <summary>One export of one media item: waiting, running, or finished in this run of the app or an earlier one.</summary>
public sealed partial class ExportJob : ObservableObject
{
    private CancellationTokenSource _cancellation = new();

    public ExportJob(MediaSearchResult media, ExportOptions options)
    {
        Media = media;
        Options = options;
        Status = "Up next";
    }

    /// <summary>An export that finished in an earlier run, as it was saved.</summary>
    public ExportJob(ExportRecord record)
        : this(record.Media, record.Options)
    {
        Files = record.Files;
        FinishedAt = record.FinishedAt;
        Progress = record.Outcome == ExportOutcome.Completed ? 100 : 0;
        Status = record.Message;
        FailureKind = record.FailureKind;
        State = record.Outcome switch
        {
            ExportOutcome.Completed => ExportJobState.Completed,
            ExportOutcome.Failed => ExportJobState.Failed,
            _ => ExportJobState.Cancelled
        };
    }

    public MediaSearchResult Media { get; }
    public ExportOptions Options { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActive), nameof(IsFinished), nameof(IsIndeterminate), nameof(Detail))]
    [NotifyPropertyChangedFor(nameof(IsQueued), nameof(IsRunning), nameof(IsCompleted), nameof(IsFailed), nameof(IsCancelled), nameof(CanRetry))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand), nameof(ShowInFolderCommand))]
    public partial ExportJobState State { get; private set; }

    /// <summary>From 0 to 100.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIndeterminate))]
    public partial double Progress { get; private set; }

    /// <summary>Where the export stands, in words: its place in the queue, how far it is, or why it failed.</summary>
    [ObservableProperty]
    public partial string Status { get; private set; }

    /// <summary>The full paths of the exported files, once it has completed.</summary>
    public IReadOnlyList<string> Files { get; private set; } = [];

    /// <summary>When it completed, failed or was cancelled. <c>null</c> until then.</summary>
    public DateTimeOffset? FinishedAt { get; private set; }
    public ExportFailureKind? FailureKind { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressDetail))]
    public partial string? Speed { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressDetail))]
    public partial string? Eta { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIndeterminate))]
    public partial ExportPhase Phase { get; private set; } = ExportPhase.Inspecting;

    /// <summary>Whether it is waiting or running, which is to say not finished.</summary>
    public bool IsActive => State is ExportJobState.Queued or ExportJobState.Running;
    public bool IsFinished => !IsActive;
    public bool IsQueued => State == ExportJobState.Queued;
    public bool IsRunning => State == ExportJobState.Running;
    public bool IsCompleted => State == ExportJobState.Completed;
    public bool IsFailed => State == ExportJobState.Failed;
    public bool IsCancelled => State == ExportJobState.Cancelled;
    public bool CanRetry => IsFailed && FailureKind != ExportFailureKind.DrmProtected;
    public string ProgressDetail => string.Join(" · ", new[] { Speed, Eta is null ? null : $"{Eta} left" }.Where(value => value is not null));

    /// <summary>Whether it is running without a percentage yet, as it is while the media is looked up.</summary>
    public bool IsIndeterminate => IsRunning && (Progress <= 0 || Phase is not ExportPhase.Downloading);

    /// <summary>Shown under the status: the file type, and the time of day it finished at.</summary>
    public string Detail
    {
        get
        {
            var format = $".{Options.Format}";
            if (FinishedAt is null)
                return format;

            var time = FinishedAt.Value.LocalDateTime.ToShortTimeString();
            return $"{format}  ·  {time}";
        }
    }

    // Screen readers announce a list item by its ToString.
    public override string ToString() => $"{Media.Title}, {Status}";

    /// <summary>The export as it is saved between runs. Only a finished export is saved.</summary>
    public ExportRecord ToRecord()
    {
        var outcome = State switch
        {
            ExportJobState.Completed => ExportOutcome.Completed,
            ExportJobState.Failed => ExportOutcome.Failed,
            _ => ExportOutcome.Cancelled
        };
        return new ExportRecord(Media, Options, outcome, Files, Status, FinishedAt ?? DateTimeOffset.Now, FailureKind);
    }

    /// <summary>Runs the export to its end. It doesn't throw: a failure becomes the job's state and status.</summary>
    public async Task RunAsync(IMediaDownloader downloader)
    {
        // It may have been cancelled while it waited.
        if (State != ExportJobState.Queued)
            return;

        State = ExportJobState.Running;
        Status = "Looking it up";
        var progress = new InlineProgress(Report);
        try
        {
            Files = await downloader.DownloadAsync(Media.SourceUrl, Options, progress, _cancellation.Token);
            Progress = 100;
            Finish(ExportJobState.Completed, "Saved");
        }
        catch (OperationCanceledException)
        {
            Finish(ExportJobState.Cancelled, "Cancelled");
        }
        catch (MediaExportException exception)
        {
            FailureKind = exception.Kind;
            Finish(ExportJobState.Failed, exception.Message);
        }
        catch (Exception exception)
        {
            Finish(ExportJobState.Failed, exception.Message);
        }
    }

    private sealed class InlineProgress(Action<ExportProgress> report) : IProgress<ExportProgress>
    {
        public void Report(ExportProgress value) => report(value);
    }

    // The state is set last, because it is the change that those watching the job act on.
    private void Finish(ExportJobState state, string status)
    {
        Status = status;
        FinishedAt = DateTimeOffset.Now;
        State = state;
    }

    private void Report(ExportProgress value)
    {
        // Progress is posted to the UI thread, so the last of it can arrive after the export has finished.
        if (State != ExportJobState.Running)
            return;

        // A video is fetched as two files, picture then sound, and the bar doesn't run backwards for the second.
        if (value.Percent is { } percent)
            Progress = Math.Max(Progress, percent);
        Phase = value.Phase;
        Speed = value.Speed;
        Eta = value.Eta;
        Status = value.Phase switch
        {
            ExportPhase.Merging => "Merging",
            ExportPhase.Converting => "Converting",
            ExportPhase.Completed => "Finishing",
            _ when value.Percent is { } => $"Downloading {Progress:0}%",
            _ => "Downloading"
        };
    }

    /// <summary>Resets a retryable failure in place and returns it to the queue.</summary>
    public bool Retry()
    {
        if (!CanRetry)
            return false;

        _cancellation.Dispose();
        _cancellation = new CancellationTokenSource();
        Files = [];
        FinishedAt = null;
        FailureKind = null;
        Progress = 0;
        Speed = null;
        Eta = null;
        Phase = ExportPhase.Inspecting;
        Status = "Up next";
        State = ExportJobState.Queued;
        return true;
    }

    [RelayCommand(CanExecute = nameof(IsActive))]
    private void Cancel()
    {
        _cancellation.Cancel();

        // A running export notices the cancellation itself. A queued one has nothing running to notice it.
        if (State == ExportJobState.Queued)
            Finish(ExportJobState.Cancelled, "Cancelled");
    }

    /// <summary>Opens the folder the export was saved to, with its file selected if it is still there.</summary>
    [RelayCommand(CanExecute = nameof(IsCompleted))]
    private void ShowInFolder()
    {
        var file = Files.FirstOrDefault();
        var folder = file is null ? Options.OutputFolder : Path.GetDirectoryName(file);
        if (File.Exists(file))
            Process.Start("explorer.exe", $"/select,\"{file}\"");
        else if (Directory.Exists(folder))
            Process.Start("explorer.exe", $"\"{folder}\"");
    }
}
