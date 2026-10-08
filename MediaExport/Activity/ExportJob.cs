// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaExport.Converters;
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

/// <summary>One export: waiting its turn, running now, or finished (this session or an earlier one).</summary>
public sealed partial class ExportJob : ObservableObject
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Stopwatch _downloading = new();

    public ExportJob(MediaSearchResult media, ExportOptions options)
    {
        Media = media;
        Options = options;
        Status = "Up next";
    }

    public ExportJob(ExportRecord record)
        : this(record.Media, record.Options)
    {
        Files = record.Files;
        FinishedAt = record.FinishedAt;
        Progress = record.Outcome == ExportOutcome.Completed ? 100 : 0;
        State = record.Outcome switch
        {
            ExportOutcome.Completed => ExportJobState.Completed,
            ExportOutcome.Failed => ExportJobState.Failed,
            _ => ExportJobState.Cancelled
        };
        Status = State == ExportJobState.Completed ? DescribeSaved() : record.Message ?? "Cancelled";
    }

    public MediaSearchResult Media { get; }
    public ExportOptions Options { get; }
    public DateTimeOffset? FinishedAt { get; private set; }

    /// <summary>What was exported, e.g. ".mp4 1080p, no sound" or ".mp3 320 kbps, 2 clips".</summary>
    public string Summary
    {
        get
        {
            string quality = Options switch
            {
                { IncludeVideo: true, MaxHeight: int height } => $"{height}p",
                { IncludeVideo: false, AudioBitrate: int kbps } => $"{kbps} kbps",
                _ => "best quality"
            };
            var parts = new List<string> { $".{Options.Format.ToLowerInvariant()} {quality}" };
            if (Options is { IncludeVideo: true, IncludeAudio: false })
                parts.Add("no sound");
            if (Options.Clips.Count > 0)
                parts.Add(Options.Clips.Count == 1 ? "1 clip" : $"{Options.Clips.Count} clips");
            return string.Join(", ", parts);
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning), nameof(IsQueued), nameof(IsCompleted), nameof(IsFailed), nameof(IsFinished))]
    [NotifyPropertyChangedFor(nameof(IsIndeterminate), nameof(ShowsProgress), nameof(DisplayProgress))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand), nameof(OpenCommand), nameof(ShowInFolderCommand))]
    public partial ExportJobState State { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIndeterminate), nameof(DisplayProgress))]
    public partial double Progress { get; private set; }

    [ObservableProperty]
    public partial string Status { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutputPath))]
    public partial IReadOnlyList<string> Files { get; private set; } = [];

    public string? OutputPath => Files.FirstOrDefault();

    /// <summary>Under way or waiting to be: not finished yet.</summary>
    public bool IsRunning => State is ExportJobState.Queued or ExportJobState.Running;
    public bool IsQueued => State == ExportJobState.Queued;
    public bool IsCompleted => State == ExportJobState.Completed;
    public bool IsFailed => State == ExportJobState.Failed;
    public bool IsFinished => !IsRunning;

    // yt-dlp reports nothing while it looks the media up or post-processes it.
    public bool IsIndeterminate => State == ExportJobState.Running && (Progress <= 0 || Progress >= 100);

    /// <summary>A failed export keeps its bar, in the error colour; one that's waiting has none yet.</summary>
    public bool ShowsProgress => State == ExportJobState.Running || IsFailed;
    public double DisplayProgress => IsFailed ? 100 : Progress;

    // Activity rows are announced by screen readers using ToString.
    public override string ToString() => $"{Media.Title}, {Summary}, {Status}";

    public ExportRecord ToRecord() => new(
        Media,
        Options,
        State switch { ExportJobState.Completed => ExportOutcome.Completed, ExportJobState.Failed => ExportOutcome.Failed, _ => ExportOutcome.Cancelled },
        Files,
        IsCompleted ? null : Status,
        FinishedAt ?? DateTimeOffset.Now);

    public async Task RunAsync(IMediaDownloader downloader)
    {
        if (!IsQueued)
            return; // Cancelled while it waited.

        State = ExportJobState.Running;
        Status = "Looking it up";
        var progress = new Progress<ExportProgress>(Report);
        try
        {
            Files = await downloader.DownloadAsync(Media.SourceUrl, Options, progress, _cancellation.Token);
            Progress = 100;
            Status = DescribeSaved();
            State = ExportJobState.Completed;
        }
        catch (OperationCanceledException)
        {
            Status = "Cancelled";
            State = ExportJobState.Cancelled;
        }
        catch (Exception exception)
        {
            Status = exception.Message;
            State = ExportJobState.Failed;
        }

        FinishedAt = DateTimeOffset.Now;
    }

    private string DescribeSaved() => Files.Count switch
    {
        0 => "Saved",
        1 => $"Saved to {Path.GetFileName(Path.GetDirectoryName(Files[0]))}",
        var count => $"Saved {count} files to {Path.GetFileName(Path.GetDirectoryName(Files[0]))}"
    };

    private void Report(ExportProgress value)
    {
        if (State != ExportJobState.Running)
            return; // Progress is posted asynchronously and can arrive after completion.

        // Video downloads fetch video then audio; the bar never runs backwards.
        Progress = value.IsProcessing ? 100 : Math.Max(Progress, value.Percent);
        if (Progress > 0)
            _downloading.Start();

        Status = value.Step ?? (Progress >= 100 ? "Finishing up" : DescribeProgress(Progress, _downloading.Elapsed));
    }

    /// <summary>
    /// How far along, and roughly how long is left: the time taken so far, scaled up to the part still to do.
    /// The estimate waits until there's enough to go on, as the first seconds say little about the rest.
    /// </summary>
    internal static string DescribeProgress(double percent, TimeSpan elapsed) =>
        percent >= 5 && elapsed >= TimeSpan.FromSeconds(3)
            ? $"{percent:0}%  ·  {Format.Remaining(elapsed * ((100 - percent) / percent))}"
            : $"{percent:0}%";

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Cancel()
    {
        _cancellation.Cancel();
        if (!IsQueued)
            return;

        // Nothing's running to notice the cancellation, so it's finished here.
        Status = "Cancelled";
        FinishedAt = DateTimeOffset.Now;
        State = ExportJobState.Cancelled;
    }

    // Several files open their folder instead; opening each would be a surprise.
    [RelayCommand(CanExecute = nameof(IsCompleted))]
    private void Open()
    {
        if (Files.Count == 1 && File.Exists(Files[0]))
            Process.Start(new ProcessStartInfo(Files[0]) { UseShellExecute = true });
        else
            ShowInFolder();
    }

    [RelayCommand(CanExecute = nameof(IsCompleted))]
    private void ShowInFolder()
    {
        if (OutputPath is { } path && File.Exists(path))
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        else if (OutputPath is { } missing && Directory.Exists(Path.GetDirectoryName(missing)))
            Process.Start("explorer.exe", $"\"{Path.GetDirectoryName(missing)}\"");
    }
}
