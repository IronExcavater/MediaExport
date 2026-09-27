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
    Running,
    Completed,
    Failed,
    Cancelled
}

/// <summary>One export: running now, or finished (this session or an earlier one).</summary>
public sealed partial class ExportJob : ObservableObject
{
    private readonly CancellationTokenSource _cancellation = new();

    public ExportJob(MediaSearchResult media, ExportOptions options)
    {
        Media = media;
        Options = options;
        Status = "Looking it up";
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

    /// <summary>What was exported, e.g. "MP4 1080p, no sound" or "MP3 320 kbps, 2 clips".</summary>
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
            var parts = new List<string> { $"{Options.Format.ToUpperInvariant()} {quality}" };
            if (Options is { IncludeVideo: true, IncludeAudio: false })
                parts.Add("no sound");
            if (Options.Clips.Count > 0)
                parts.Add(Options.Clips.Count == 1 ? "1 clip" : $"{Options.Clips.Count} clips");
            return string.Join(", ", parts);
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning), nameof(IsCompleted), nameof(IsFailed), nameof(IsFinished))]
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

    public bool IsRunning => State == ExportJobState.Running;
    public bool IsCompleted => State == ExportJobState.Completed;
    public bool IsFailed => State == ExportJobState.Failed;
    public bool IsFinished => !IsRunning;

    // yt-dlp reports nothing while it looks the media up or post-processes it.
    public bool IsIndeterminate => IsRunning && (Progress <= 0 || Progress >= 100);

    /// <summary>A failed export keeps its bar, in the error colour.</summary>
    public bool ShowsProgress => IsRunning || IsFailed;
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
        if (!IsRunning)
            return; // Progress is posted asynchronously and can arrive after completion.

        // Video downloads fetch video then audio; the bar never runs backwards.
        Progress = value.IsProcessing ? 100 : Math.Max(Progress, value.Percent);
        Status = value.Step
            ?? (Progress >= 100 ? "Finishing up"
                : value.Speed is { } speed && !speed.StartsWith("0.00", StringComparison.Ordinal) ? $"{Progress:0}% at {speed}"
                : $"{Progress:0}%");
    }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Cancel() => _cancellation.Cancel();

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
