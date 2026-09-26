// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaExport.Core.Exporting;
using MediaExport.Core.Media.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MediaExport.Export;

public enum ExportJobState
{
    Running,
    Completed,
    Failed,
    Cancelled
}

public sealed partial class ExportJob(MediaSearchResult media, ExportOptions options) : ObservableObject
{
    private readonly CancellationTokenSource _cancellation = new();

    public MediaSearchResult Media { get; } = media;
    public ExportOptions Options { get; } = options;

    public string Summary => Options switch
    {
        { Kind: MediaKind.Audio } => $"{Options.Format.ToUpperInvariant()} audio",
        { MaxHeight: int height } => $"{Options.Format.ToUpperInvariant()} · {height}p",
        _ => $"{Options.Format.ToUpperInvariant()} · Best quality"
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning), nameof(IsCompleted), nameof(IsFailed), nameof(IsCancelled))]
    [NotifyPropertyChangedFor(nameof(IsIndeterminate), nameof(ShowsProgress), nameof(DisplayProgress))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand), nameof(OpenCommand), nameof(ShowInFolderCommand))]
    public partial ExportJobState State { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIndeterminate), nameof(DisplayProgress))]
    public partial double Progress { get; private set; }

    [ObservableProperty]
    public partial string Status { get; private set; } = "Preparing";

    [ObservableProperty]
    public partial string? OutputPath { get; private set; }

    /// <summary>Whether Activity shows this export with its player.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExpandGlyph))]
    public partial bool IsExpanded { get; set; }

    public string ExpandGlyph => IsExpanded ? "" : "";

    // Activity rows are announced by screen readers using ToString.
    public override string ToString() => $"{Media.Title}, {Summary}, {Status}";

    public bool IsRunning => State == ExportJobState.Running;
    public bool IsCompleted => State == ExportJobState.Completed;
    public bool IsFailed => State == ExportJobState.Failed;
    public bool IsCancelled => State == ExportJobState.Cancelled;

    // yt-dlp reports nothing while it resolves the media or post-processes it.
    public bool IsIndeterminate => IsRunning && (Progress <= 0 || Progress >= 100);

    /// <summary>A failed export keeps its bar, in the error colour, at the point it stopped.</summary>
    public bool ShowsProgress => IsRunning || IsFailed;
    public double DisplayProgress => IsFailed && Progress <= 0 ? 100 : Progress;

    public async Task RunAsync(IMediaDownloader downloader)
    {
        var progress = new Progress<ExportProgress>(Report);
        try
        {
            OutputPath = await downloader.DownloadAsync(Media.SourceUrl, Options, progress, _cancellation.Token);
            Progress = 100;
            Status = $"Saved to {Path.GetFileName(Path.GetDirectoryName(OutputPath))}";
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
    }

    private void Report(ExportProgress value)
    {
        if (!IsRunning)
            return; // Progress is posted asynchronously and can arrive after completion.

        // Video exports download separate video and audio streams; never let the bar run backwards.
        Progress = value.IsProcessing ? 100 : Math.Max(Progress, value.Percent);
        Status = Progress >= 100
            ? "Finishing up"
            : value.Speed is { } speed && !speed.StartsWith("0.00", StringComparison.Ordinal)
                ? $"{Progress:0}% · {speed}"
                : $"{Progress:0}%";
    }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Cancel() => _cancellation.Cancel();

    [RelayCommand(CanExecute = nameof(IsCompleted))]
    private void Open() =>
        Process.Start(new ProcessStartInfo(OutputPath!) { UseShellExecute = true });

    [RelayCommand(CanExecute = nameof(IsCompleted))]
    private void ShowInFolder() =>
        Process.Start("explorer.exe", $"/select,\"{OutputPath}\"");
}
