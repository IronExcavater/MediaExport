// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaExport.Activity;
using MediaExport.Core.Exporting;
using MediaExport.Core.Media.Models;
using MediaExport.Core.Media.Providers;
using MediaExport.Core.Settings;
using MediaExport.Core.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MediaExport.Export;

/// <summary>Media that was opened earlier, as it is listed for opening again.</summary>
public sealed record ViewedMedia(MediaSearchResult Media, bool IsPinned = false) : IPinnable<ViewedMedia>
{
    public ViewedMedia WithPin(bool isPinned) => this with { IsPinned = isPinned };
}

/// <summary>The media that's open and how it's to be exported. Exporting hands the job to <see cref="ActivityViewModel"/>.</summary>
public sealed partial class ExportViewModel : ObservableObject
{
    private const int ViewedCapacity = 8;

    private const string VideoModeName = "Video";
    private const string AudioOnlyModeName = "Audio only";

    [ObservableProperty]
    public partial string SelectedOutputFolder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Downloads");

    public IReadOnlyList<string> ExportModes => IsAudioSource ? AudioOnlyMode : AudioVideoModes;
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(IsAudioExport),
        nameof(AvailableFormats))]
    public partial string SelectedExportMode { get; set; } = VideoModeName;
    public bool IsAudioSource => Media?.Kind == MediaKind.Audio;
    public bool IsAudioExport => SelectedExportMode == AudioOnlyModeName;

    private static readonly IReadOnlyList<string> AudioFormats = ["mp3", "wav", "flac", "m4a"];
    private static readonly IReadOnlyList<string> VideoFormats = ["mp4", "mkv", "webm"];
    private static readonly IReadOnlyList<string> AudioOnlyMode = [AudioOnlyModeName];
    private static readonly IReadOnlyList<string> AudioVideoModes = [VideoModeName, AudioOnlyModeName];
    public IReadOnlyList<string> AudioBitrates { get; } = ["128 kbps", "192 kbps", "256 kbps", "320 kbps"];
    public IReadOnlyList<string> VideoResolutions { get; } = ["Best available", "1080p", "720p", "480p"];

    private readonly ActivityViewModel _activity;
    private readonly SettingsStore _settings;
    private readonly IReadOnlyList<IMediaSearchProvider> _providers;
    private readonly IMediaDownloader _downloader;

    // The link being read. A newer one replaces it, and what an older one finds is ignored.
    private CancellationTokenSource? _reading;

    public ExportViewModel(
        ActivityViewModel activity,
        SettingsStore settings,
        IEnumerable<IMediaSearchProvider> providers,
        IMediaDownloader downloader)
    {
        _activity = activity;
        _settings = settings;
        _providers = providers.ToList();
        _downloader = downloader;

        if (!string.IsNullOrWhiteSpace(settings.Current.ExportFolder))
            SelectedOutputFolder = settings.Current.ExportFolder;
    }

    /// <summary>The media that is open, or <c>null</c> when there is none or it is still being read from a link.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(IsOpen),
        nameof(IsAudioSource),
        nameof(ExportModes),
        nameof(AvailableFormats))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    public partial MediaSearchResult? Media { get; private set; }

    [ObservableProperty]
    public partial string SelectedFormat { get; set; } = "mp4";
    [ObservableProperty]
    public partial string SelectedAudioBitrate { get; set; } = "192 kbps";
    [ObservableProperty]
    public partial string SelectedVideoResolution { get; set; } = "Best available";

    public IReadOnlyList<string> AvailableFormats => IsAudioExport ? AudioFormats : VideoFormats;

    /// <summary>The media that was opened, newest first after the pinned ones.</summary>
    public RecentList<ViewedMedia> RecentlyViewed { get; } = new(ViewedCapacity, IsSame);

    private static bool IsSame(ViewedMedia one, ViewedMedia other) => one.Media.SourceUrl == other.Media.SourceUrl;

    /// <summary>Whether a link is being read.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpen))]
    public partial bool IsLoading { get; private set; }

    /// <summary>Whether there is media to show, or will be once its link has been read.</summary>
    public bool IsOpen => Media is not null || IsLoading;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    [NotifyPropertyChangedFor(nameof(IsExportBusy), nameof(CanEditExport), nameof(IsExportProgressIndeterminate), nameof(ExportActionText))]
    public partial bool IsInspecting { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    [NotifyPropertyChangedFor(nameof(CanEditExport))]
    public partial bool IsAvailable { get; private set; } = true;

    [ObservableProperty]
    public partial string? AvailabilityMessage { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    [NotifyPropertyChangedFor(nameof(IsExporting), nameof(IsExportBusy), nameof(CanEditExport), nameof(ExportProgress))]
    [NotifyPropertyChangedFor(nameof(IsExportProgressIndeterminate), nameof(ExportActionText), nameof(ExportProgressDetail))]
    public partial ExportJob? CurrentJob { get; private set; }

    public bool IsExporting => CurrentJob?.IsActive == true;
    public bool IsExportBusy => IsInspecting || IsExporting;
    public bool CanEditExport => IsAvailable && !IsExportBusy;
    public double ExportProgress => CurrentJob?.Progress ?? 0;
    public bool IsExportProgressIndeterminate => IsInspecting || CurrentJob?.IsIndeterminate == true;
    public string ExportProgressDetail => CurrentJob?.ProgressDetail ?? string.Empty;
    public string ExportActionText => IsInspecting
        ? "Checking media"
        : CurrentJob switch
        {
            { IsActive: true } => CurrentJob.Status,
            { IsCompleted: true } => "Export again",
            { IsFailed: true } => "Try again",
            { IsCancelled: true } => "Export again",
            _ => "Export"
        };

    /// <summary>Raised when media is opened or a link starts being read, for the window to show the Export page.</summary>
    public event EventHandler? Opened;

    private bool CanExport() => Media is not null && IsAvailable && !IsInspecting && !IsExporting;

    partial void OnCurrentJobChanged(ExportJob? oldValue, ExportJob? newValue)
    {
        if (oldValue is not null)
            oldValue.PropertyChanged -= CurrentJob_PropertyChanged;
        if (newValue is not null)
            newValue.PropertyChanged += CurrentJob_PropertyChanged;
    }

    private void CurrentJob_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        OnPropertyChanged(nameof(IsExporting));
        OnPropertyChanged(nameof(IsExportBusy));
        OnPropertyChanged(nameof(CanEditExport));
        OnPropertyChanged(nameof(ExportProgress));
        OnPropertyChanged(nameof(IsExportProgressIndeterminate));
        OnPropertyChanged(nameof(ExportActionText));
        OnPropertyChanged(nameof(ExportProgressDetail));
        ExportCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedOutputFolderChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || _settings.Current.ExportFolder == value)
            return;

        _settings.Current.ExportFolder = value;
        _settings.Save();
    }

    /// <summary>Chooses valid defaults when new media is opened.</summary>
    partial void OnMediaChanged(MediaSearchResult? value)
    {
        if (value is null)
            return;

        SelectedExportMode = value.Kind == MediaKind.Audio
            ? AudioOnlyModeName
            : VideoModeName;
        SelectedFormat = IsAudioExport ? "mp3" : "mp4";
        SelectedAudioBitrate = "192 kbps";
        SelectedVideoResolution = "Best available";
    }

    /// <summary>Keeps the selected format valid when the export type changes.</summary>
    partial void OnSelectedExportModeChanged(string value)
    {
        if (IsAudioSource && value != AudioOnlyModeName)
        {
            SelectedExportMode = AudioOnlyModeName;
            return;
        }

        SelectedFormat = IsAudioExport ? "mp3" : "mp4";
    }

    /// <summary>Queues an export of the open media. How it gets on is shown on the Activity page.</summary>
    [RelayCommand(CanExecute = nameof(CanExport))]
    private void Export()
    {
        if (Media is null)
            return;

        var format = SelectedFormat.Trim().ToLowerInvariant();
        var bitrate = IsAudioExport
            ? int.Parse(
                SelectedAudioBitrate.Replace(" kbps", ""),
                System.Globalization.CultureInfo.InvariantCulture)
            : 192;

        var videoHeight = IsAudioExport ? 0 : SelectedVideoResolution switch
        {
            "1080p" => 1080,
            "720p" => 720,
            "480p" => 480,
            _ => 0
        };

        var options = new ExportOptions(
            format,
            SelectedOutputFolder,
            bitrate,
            videoHeight);

        CurrentJob = _activity.Start(Media!, options);
    }

    public void Open(MediaSearchResult media, ExportOptions? options = null)
    {
        _reading?.Cancel();
        _reading = null;
        IsLoading = false;
        CurrentJob = null;
        Media = media;
        if (options is not null)
            Restore(options);
        RecentlyViewed.Remember(new ViewedMedia(media));
        _ = InspectAsync(media);
        Opened?.Invoke(this, EventArgs.Empty);
    }

    private void Restore(ExportOptions options)
    {
        SelectedOutputFolder = options.OutputFolder;
        var format = options.Format.ToLowerInvariant();
        SelectedExportMode = AudioFormats.Contains(format) ? AudioOnlyModeName : VideoModeName;
        SelectedFormat = format;
        SelectedAudioBitrate = $"{options.AudioBitrateKbps} kbps";
        SelectedVideoResolution = options.VideoHeight == 0 ? "Best available" : $"{options.VideoHeight}p";
    }

    private async Task InspectAsync(MediaSearchResult media)
    {
        IsInspecting = true;
        IsAvailable = false;
        AvailabilityMessage = null;
        try
        {
            var availability = await _downloader.InspectAsync(media.SourceUrl);
            if (Media != media)
                return;
            IsAvailable = availability.CanExport;
            AvailabilityMessage = availability.Message;
        }
        catch (Exception exception)
        {
            if (Media == media)
                AvailabilityMessage = exception.Message;
        }
        finally
        {
            if (Media == media)
                IsInspecting = false;
        }
    }

    /// <summary>Clears the media from the Export page without removing it from Recently viewed.</summary>
    public void Close()
    {
        _reading?.Cancel();
        _reading = null;
        IsLoading = false;
        IsInspecting = false;
        AvailabilityMessage = null;
        CurrentJob = null;
        Media = null;
    }

    /// <summary>Opens the media a link is to. The page shows at once, and the media when the link has been read.</summary>
    /// <returns>Why the link couldn't be opened, or <c>null</c> if it was, or if a newer link took its place.</returns>
    public async Task<string?> OpenLinkAsync(Uri link)
    {
        _reading?.Cancel();
        var reading = new CancellationTokenSource();
        _reading = reading;
        Media = null;
        IsLoading = true;
        Opened?.Invoke(this, EventArgs.Empty);

        try
        {
            var media = await FindAsync(link, reading.Token);
            if (reading != _reading)
                return null;

            Media = media;
            if (media is not null)
            {
                RecentlyViewed.Remember(new ViewedMedia(media));
                await InspectAsync(media);
                return null;
            }

            // No provider had one item at the link, either because it is to another site or to a list.
            var sources = string.Join(" or ", _providers.Select(provider => provider.DisplayName));
            return $"Couldn't open that link. It needs to be to one video or track on {sources}.";
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (InvalidOperationException exception)
        {
            return reading == _reading ? $"Couldn't open that link. {exception.Message}" : null;
        }
        finally
        {
            if (reading == _reading)
                IsLoading = false;
        }
    }

    // Each provider says whether the link is to its source, and the one it is to reads it.
    private async Task<MediaSearchResult?> FindAsync(Uri link, CancellationToken cancellationToken)
    {
        foreach (var provider in _providers)
        {
            var media = await provider.FindByLinkAsync(link, cancellationToken);
            if (media is not null)
                return media;
        }
        return null;
    }
}
