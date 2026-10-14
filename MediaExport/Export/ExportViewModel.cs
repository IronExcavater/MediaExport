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
    public partial string SelectedOutputFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    public IReadOnlyList<string> ExportModes => IsAudioSource ? AudioOnlyMode : AudioVideoModes;
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(IsAudioExport),
        nameof(IsVideoExport),
        nameof(AvailableFormats))]
    public partial string SelectedExportMode { get; set; } = VideoModeName;
    public bool IsAudioSource => Media?.Kind == MediaKind.Audio;
    public bool IsAudioExport => SelectedExportMode == AudioOnlyModeName;
    public bool IsVideoExport => !IsAudioExport;

    private static readonly IReadOnlyList<string> AudioFormats = ["mp3", "wav", "flac", "m4a"];
    private static readonly IReadOnlyList<string> VideoFormats = ["mp4", "mkv", "webm"];
    private static readonly IReadOnlyList<string> AudioOnlyMode = [AudioOnlyModeName];
    private static readonly IReadOnlyList<string> AudioVideoModes = [VideoModeName, AudioOnlyModeName];
    public IReadOnlyList<string> AudioBitrates { get; } = ["128 kbps", "192 kbps", "256 kbps", "320 kbps"];
    public IReadOnlyList<string> VideoResolutions { get; } = ["Best available", "1080p", "720p", "480p"];

    private readonly ActivityViewModel _activity;
    private readonly SettingsStore _settings;
    private readonly IReadOnlyList<IMediaSearchProvider> _providers;

    // The link being read. A newer one replaces it, and what an older one finds is ignored.
    private CancellationTokenSource? _reading;

    public ExportViewModel(
        ActivityViewModel activity,
        SettingsStore settings,
        IEnumerable<IMediaSearchProvider> providers)
    {
        _activity = activity;
        _settings = settings;
        _providers = providers.ToList();
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
    public bool IsAudio => Media?.Kind == MediaKind.Audio;
    public bool IsVideo => Media is not null && Media.Kind != MediaKind.Audio;

    /// <summary>The media that was opened, newest first after the pinned ones.</summary>
    public RecentList<ViewedMedia> RecentlyViewed { get; } = new(ViewedCapacity, IsSame);

    private static bool IsSame(ViewedMedia one, ViewedMedia other) => one.Media.SourceUrl == other.Media.SourceUrl;

    /// <summary>Whether a link is being read.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpen))]
    public partial bool IsLoading { get; private set; }

    /// <summary>Whether there is media to show, or will be once its link has been read.</summary>
    public bool IsOpen => Media is not null || IsLoading;

    /// <summary>Raised when media is opened or a link starts being read, for the window to show the Export page.</summary>
    public event EventHandler? Opened;

    private bool CanExport() => Media is not null;

    /// <summary>Triggers when media changes or is intially set.</summary>
    partial void OnMediaChanged(MediaSearchResult? value)
    {
        if (value is null)
            return;

        // Notify the UI that the available options have changed.
        OnPropertyChanged(nameof(IsAudioSource));
        OnPropertyChanged(nameof(ExportModes));

        // Select a valid mode for this media.
        SelectedExportMode = value.Kind == MediaKind.Audio
            ? AudioOnlyModeName
            : VideoModeName;

        // Refresh the available formats before choosing one.
        OnPropertyChanged(nameof(AvailableFormats));

        // Select a valid format for the chosen mode.
        SelectedFormat = SelectedExportMode == AudioOnlyModeName
            ? "mp3"
            : "mp4";

        SelectedAudioBitrate = "192 kbps";
        SelectedVideoResolution = "Best available";

        LogExportSelection("Media changed");

    }

    /// <summary>Triggers when a new export mode is selected by the user.</summary>
    partial void OnSelectedExportModeChanged(string value)
    {
        if (IsAudioSource && value != AudioOnlyModeName)
        {
            SelectedExportMode = AudioOnlyModeName;
            return;
        }

        // Notify the UI that the available formats may have changed.
        OnPropertyChanged(nameof(IsAudioExport));
        OnPropertyChanged(nameof(IsVideoExport));
        OnPropertyChanged(nameof(AvailableFormats));

        // Choose a valid default format for the new mode.
        SelectedFormat = IsAudioExport ? "mp3" : "mp4";
    }

    /// <summary>Queues an export of the open media. How it gets on is shown on the Activity page.</summary>
    [RelayCommand(CanExecute = nameof(CanExport))]
    private void Export()
    {
        if (Media is null)
            return;

        var format = IsAudioExport
            ? SelectedFormat.Trim().ToLowerInvariant()
            : SelectedFormat.Trim().ToLowerInvariant();

        var userFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var bitrate = int.Parse(
            SelectedAudioBitrate.Replace(" kbps", ""),
            System.Globalization.CultureInfo.InvariantCulture);

        var videoHeight = SelectedVideoResolution switch
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

        _activity.Start(Media!, options);
    }

    public void Open(MediaSearchResult media)
    {
        _reading?.Cancel();
        _reading = null;
        IsLoading = false;
        Media = media;
        RecentlyViewed.Remember(new ViewedMedia(media));
        Opened?.Invoke(this, EventArgs.Empty);
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

    private void LogExportSelection(string stage)
    {
        System.Diagnostics.Debug.WriteLine(
            $"[{stage}] " +
            $"Media: {Media?.Kind}, " +
            $"Mode: '{SelectedExportMode}', " +
            $"Format: '{SelectedFormat}', " +
            $"Modes: [{string.Join(", ", ExportModes)}], " +
            $"Formats: [{string.Join(", ", AvailableFormats)}]");
    }
}
