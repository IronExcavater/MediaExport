// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaExport.Activity;
using MediaExport.Core.Exporting;
using MediaExport.Core.Media.Models;
using MediaExport.Core.Settings;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MediaExport.Export;

public enum ExportState
{
    /// <summary>Nothing chosen yet.</summary>
    Empty,
    Checking,
    Ready,
    /// <summary>It can't be exported; <see cref="ExportViewModel.StatusText"/> says why.</summary>
    Blocked,
    Running,
    Done,
    Failed
}

public sealed partial class SubtitleChoice(SubtitleTrack track) : ObservableObject
{
    public SubtitleTrack Track { get; } = track;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public override string ToString() => Track.Name;
}

/// <summary>The media being exported and every choice about how.</summary>
public sealed partial class ExportViewModel : ObservableObject
{
    private const string Best = "best";

    private readonly IMediaDownloader _downloader;
    private readonly ActivityViewModel _activity;
    private readonly SettingsStore _settings;
    private readonly Dictionary<string, MediaDetails> _details = [];
    private CancellationTokenSource? _inspection;
    private bool _isRestoring;

    // The options a finished job was made with count as "done" only until something changes.
    private int _optionsVersion;
    private int _jobOptionsVersion;

    public ExportViewModel(IMediaDownloader downloader, ActivityViewModel activity, SettingsStore settings)
    {
        _downloader = downloader;
        _activity = activity;
        _settings = settings;
        AppSettings saved = settings.Current;
        _isRestoring = true; // Otherwise each choice restored here would save the rest before they're restored.

        Kind = new ChoiceGroup(
            [new Choice("video", "Video", "Picture and sound"), new Choice("audio", "Audio only", "Just the sound")],
            saved.IsAudioExport ? "audio" : "video");
        VideoFormat = new ChoiceGroup(
            [new Choice("mp4", "MP4", "Plays almost everywhere"), new Choice("mkv", "MKV", "Keeps every subtitle and track"), new Choice("webm", "WebM", "Open format, often smaller")],
            saved.VideoFormat);
        AudioFormat = new ChoiceGroup(
            [new Choice("mp3", "MP3", "Plays almost everywhere"), new Choice("m4a", "M4A", "Apple devices and iTunes"), new Choice("opus", "Opus", "Smallest at the same quality"),
             new Choice("flac", "FLAC", "Lossless copy of what's there"), new Choice("wav", "WAV", "Uncompressed, for editing")],
            saved.AudioFormat);
        VideoQuality = new ChoiceGroup(VideoQualities(null), saved.VideoQuality?.ToString(CultureInfo.InvariantCulture) ?? Best);
        AudioQuality = new ChoiceGroup(
            [new Choice(Best, "Best"), new Choice("320", "320 kbps"), new Choice("256", "256 kbps"), new Choice("192", "192 kbps"), new Choice("128", "128 kbps")],
            saved.AudioBitrate?.ToString(CultureInfo.InvariantCulture) ?? Best);
        FileName = new ChoiceGroup(
            [new Choice("%(title)s", "Title"), new Choice("%(uploader)s - %(title)s", "Creator - Title"), new Choice("%(title)s [%(id)s]", "Title [ID]")],
            saved.FileNameTemplate);

        OutputFolder = saved.OutputFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        EmbedDetails = saved.EmbedMetadata && saved.EmbedThumbnail;
        EmbedChapters = saved.EmbedChapters;
        RemoveSponsors = saved.RemoveSponsors;
        _isRestoring = false;

        foreach (ChoiceGroup group in new[] { Kind, VideoFormat, AudioFormat, VideoQuality, AudioQuality, FileName })
            group.Changed += (_, _) => OnOptionChanged();
        Clips.CollectionChanged += (_, _) => OnOptionChanged();
        activity.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ActivityViewModel.RunningCount))
                RefreshState();
        };
    }

    public ChoiceGroup Kind { get; }
    public ChoiceGroup VideoFormat { get; }
    public ChoiceGroup AudioFormat { get; }
    public ChoiceGroup VideoQuality { get; }
    public ChoiceGroup AudioQuality { get; }
    public ChoiceGroup FileName { get; }

    public ObservableCollection<SubtitleChoice> Subtitles { get; } = [];

    /// <summary>Parts to keep, in order; empty keeps all of it.</summary>
    public ObservableCollection<Clip> Clips { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMedia), nameof(Duration))]
    public partial MediaSearchResult? Media { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Chapters), nameof(HasChapters), nameof(HasSubtitles), nameof(CanRemoveSponsors))]
    public partial MediaDetails? Details { get; private set; }

    [ObservableProperty]
    public partial string OutputFolder { get; set; }

    [ObservableProperty]
    public partial bool EmbedDetails { get; set; }

    [ObservableProperty]
    public partial bool EmbedChapters { get; set; }

    [ObservableProperty]
    public partial bool RemoveSponsors { get; set; }

    [ObservableProperty]
    public partial bool SplitChapters { get; set; }

    [ObservableProperty]
    public partial bool JoinClips { get; set; } = true;

    /// <summary>The export shown on the button: running, or finished with the current options.</summary>
    [ObservableProperty]
    public partial ExportJob? Job { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning), nameof(IsDone), nameof(IsFailed), nameof(IsBlocked), nameof(CanStart), nameof(IsBusy), nameof(ExportGlyph))]
    public partial ExportState State { get; private set; }

    [ObservableProperty]
    public partial string? StatusText { get; private set; }

    public bool HasMedia => Media is not null;
    public bool IsAudio => Kind.SelectedKey == "audio";
    public bool IsVideo => !IsAudio;
    public bool CanExportVideo => Details?.HasVideo ?? Media?.Kind != MediaKind.Audio;
    public bool HasChapters => Chapters.Count > 0;
    public bool HasSubtitles => Subtitles.Count > 0;
    public bool CanRemoveSponsors => Details?.SupportsSponsorBlock == true;
    public bool HasClips => Clips.Count > 0;
    public bool HasSeveralClips => Clips.Count > 1;
    public TimeSpan Duration => Media?.Duration ?? TimeSpan.Zero;
    public IReadOnlyList<Chapter> Chapters => Details?.Chapters ?? [];

    public bool IsRunning => State == ExportState.Running;
    public bool IsDone => State == ExportState.Done;
    public bool IsFailed => State == ExportState.Failed;
    public bool IsBlocked => State == ExportState.Blocked;
    public bool IsBusy => State is ExportState.Checking;
    public bool CanStart => State is ExportState.Ready or ExportState.Failed;
    public bool IsIndeterminate => IsRunning && Job?.IsIndeterminate == true;
    public string ExportGlyph => IsFailed ? "" : "";

    public string ExportLabel => State switch
    {
        ExportState.Failed => "Try again",
        _ when Clips.Count == 1 => "Export clip",
        _ when Clips.Count > 1 => JoinClips ? $"Export {Clips.Count} clips as one" : $"Export {Clips.Count} clips",
        _ => IsAudio ? "Export audio" : "Export video"
    };

    public double ProgressFraction => Job is { } job && (IsRunning || IsFailed) ? job.DisplayProgress / 100 : 0;
    public string? JobStatus => Job?.Status;

    public string ClipsSummary => Clips.Count switch
    {
        0 => "The whole thing",
        1 => $"{Format(Clips[0].Start)} to {Format(Clips[0].End)}",
        var count => $"{count} clips, {Format(TimeSpan.FromTicks(Clips.Sum(clip => clip.Length.Ticks)))} in all"
    };

    public string FileNamePreview
    {
        get
        {
            string name = (FileName.SelectedKey ?? "%(title)s")
                .Replace("%(title)s", Media?.Title ?? "Title")
                .Replace("%(uploader)s", Media?.Creator ?? "Creator")
                .Replace("%(id)s", Media?.Identity ?? "id");
            string extension = (IsAudio ? AudioFormat : VideoFormat).SelectedKey ?? "mp4";
            return Clips.Count > 1 && !JoinClips ? $"{name} (part 1).{extension}, …" : $"{name}.{extension}";
        }
    }

    /// <summary>Shows <paramref name="media"/>, with its details if they're already known.</summary>
    public void Open(MediaSearchResult media, MediaDetails? details = null)
    {
        if (details is not null)
            _details[media.SourceUrl] = details;

        _isRestoring = true;
        Media = media;
        Clips.Clear();
        SplitChapters = false;
        Details = null;
        Subtitles.Clear();
        if (media.Kind == MediaKind.Audio)
            Kind.Select("audio");
        _isRestoring = false;

        Job = _activity.FindRunning(media);
        _ = InspectAsync(media);
        RefreshAll();
    }

    public void Close()
    {
        _inspection?.Cancel();
        Media = null;
        Details = null;
        Job = null;
        Clips.Clear();
        RefreshAll();
    }

    /// <summary>Opens a finished export's media with the same choices, ready to go again.</summary>
    public void OpenAgain(ExportJob job)
    {
        Open(job.Media);
        ExportOptions options = job.Options;
        Kind.Select(options.Kind == MediaKind.Audio ? "audio" : "video");
        (options.Kind == MediaKind.Audio ? AudioFormat : VideoFormat).Select(options.Format);
        VideoQuality.Select(options.MaxHeight?.ToString(CultureInfo.InvariantCulture) ?? Best);
        AudioQuality.Select(options.AudioBitrate?.ToString(CultureInfo.InvariantCulture) ?? Best);
        foreach (Clip clip in options.Clips)
            Clips.Add(clip);
        JoinClips = options.JoinClips;
        RemoveSponsors = options.RemoveSponsors;
    }

    [RelayCommand]
    private void Export()
    {
        if (!CanStart || Media is null)
            return;

        _jobOptionsVersion = _optionsVersion;
        Job = _activity.Start(Media, CreateOptions());
    }

    [RelayCommand]
    private void Cancel() => Job?.CancelCommand.Execute(null);

    [RelayCommand]
    private void ExportAgain()
    {
        _optionsVersion++;
        RefreshState();
        Export();
    }

    [RelayCommand]
    private void ClearClips() => Clips.Clear();

    [RelayCommand]
    private void OpenFolder() =>
        System.Diagnostics.Process.Start("explorer.exe", $"\"{OutputFolder}\"");

    internal ExportOptions CreateOptions()
    {
        bool audio = IsAudio;
        return new ExportOptions(
            audio ? MediaKind.Audio : MediaKind.Video,
            (audio ? AudioFormat : VideoFormat).SelectedKey ?? (audio ? "mp3" : "mp4"),
            audio ? null : ParseNumber(VideoQuality.SelectedKey),
            OutputFolder)
        {
            AudioBitrate = audio ? ParseNumber(AudioQuality.SelectedKey) : null,
            EmbedMetadata = EmbedDetails,
            EmbedThumbnail = EmbedDetails,
            EmbedChapters = EmbedChapters && Clips.Count == 0,
            SubtitleLanguages = audio ? [] : Subtitles.Where(choice => choice.IsSelected).Select(choice => choice.Track.Language).ToArray(),
            Clips = Clips.ToArray(),
            JoinClips = JoinClips,
            SplitChapters = SplitChapters && HasChapters && Clips.Count == 0,
            RemoveSponsors = RemoveSponsors && CanRemoveSponsors,
            CookiesFromBrowser = _settings.Current.CookiesFromBrowser,
            FileNameTemplate = FileName.SelectedKey ?? "%(title)s"
        };
    }

    partial void OnJobChanged(ExportJob? oldValue, ExportJob? newValue)
    {
        if (oldValue is not null)
            oldValue.PropertyChanged -= OnJobPropertyChanged;
        if (newValue is not null)
            newValue.PropertyChanged += OnJobPropertyChanged;
        RefreshState();
    }

    private void OnJobPropertyChanged(object? sender, PropertyChangedEventArgs args) => RefreshState();

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(OutputFolder) or nameof(EmbedDetails) or nameof(EmbedChapters)
            or nameof(RemoveSponsors) or nameof(SplitChapters) or nameof(JoinClips))
        {
            OnOptionChanged();
        }
    }

    private void OnOptionChanged()
    {
        if (_isRestoring)
            return;

        _optionsVersion++;
        Remember();
        RefreshAll();
    }

    private void Remember()
    {
        AppSettings saved = _settings.Current;
        saved.IsAudioExport = IsAudio;
        saved.VideoFormat = VideoFormat.SelectedKey ?? saved.VideoFormat;
        saved.AudioFormat = AudioFormat.SelectedKey ?? saved.AudioFormat;
        saved.VideoQuality = ParseNumber(VideoQuality.SelectedKey);
        saved.AudioBitrate = ParseNumber(AudioQuality.SelectedKey);
        saved.FileNameTemplate = FileName.SelectedKey ?? saved.FileNameTemplate;
        saved.OutputFolder = OutputFolder;
        saved.EmbedMetadata = saved.EmbedThumbnail = EmbedDetails;
        saved.EmbedChapters = EmbedChapters;
        saved.RemoveSponsors = RemoveSponsors;
        if (HasSubtitles)
            saved.SubtitleLanguageCodes = Subtitles.Where(choice => choice.IsSelected).Select(choice => choice.Track.Language).ToList();
        _settings.Save();
    }

    private async Task InspectAsync(MediaSearchResult media)
    {
        _inspection?.Cancel();
        if (_details.TryGetValue(media.SourceUrl, out MediaDetails? known))
        {
            ApplyDetails(known);
            return;
        }

        var inspection = new CancellationTokenSource();
        _inspection = inspection;
        RefreshState();
        try
        {
            MediaDetails details = await _downloader.InspectAsync(media.SourceUrl, inspection.Token);
            _details[media.SourceUrl] = details;
            if (!inspection.IsCancellationRequested && Media == media)
                ApplyDetails(details);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Details only refine the choices; the export still reports its own errors.
            if (Media == media)
                ApplyDetails(new MediaDetails(null, [], null, [], [], false));
        }
        catch (OperationCanceledException) { }
    }

    private void ApplyDetails(MediaDetails details)
    {
        _isRestoring = true;
        Details = details;

        Kind.Items[0].IsEnabled = CanExportVideo;
        if (!CanExportVideo)
            Kind.Select("audio");

        VideoQuality.Reset(VideoQualities(details), VideoQuality.SelectedKey ?? _settings.Current.VideoQuality?.ToString(CultureInfo.InvariantCulture));
        // Remembered heights the media doesn't have fall back to the tallest it does, not to "Best".
        if (_settings.Current.VideoQuality is int height && VideoQuality.SelectedKey == Best && details.VideoQualities.Count > 0)
            VideoQuality.Select(details.VideoQualities.FirstOrDefault(quality => quality.Height <= height)?.Height.ToString(CultureInfo.InvariantCulture) ?? Best);

        Subtitles.Clear();
        foreach (SubtitleTrack track in details.Subtitles)
            Subtitles.Add(new SubtitleChoice(track) { IsSelected = _settings.Current.SubtitleLanguageCodes.Contains(track.Language) });
        foreach (SubtitleChoice choice in Subtitles)
            choice.PropertyChanged += (_, _) => OnOptionChanged();

        _isRestoring = false;
        RefreshAll();
    }

    private static IEnumerable<Choice> VideoQualities(MediaDetails? details)
    {
        yield return new Choice(Best, "Best");
        IEnumerable<(int Height, long? Bytes)> heights = details is { VideoQualities.Count: > 0 }
            ? details.VideoQualities.Select(quality => (quality.Height, quality.ApproximateBytes))
            : new[] { 2160, 1440, 1080, 720, 480 }.Select(height => (height, (long?)null));

        foreach ((int height, long? bytes) in heights)
            yield return new Choice(height.ToString(CultureInfo.InvariantCulture), Label(height)) { Detail = bytes is long size ? FormatSize(size) : null };

        static string Label(int height) => height switch
        {
            >= 2160 => $"4K",
            >= 1440 => "1440p",
            _ => $"{height}p"
        };
    }

    private void RefreshAll()
    {
        OnPropertyChanged(nameof(IsAudio));
        OnPropertyChanged(nameof(IsVideo));
        OnPropertyChanged(nameof(CanExportVideo));
        OnPropertyChanged(nameof(HasClips));
        OnPropertyChanged(nameof(HasSeveralClips));
        OnPropertyChanged(nameof(ClipsSummary));
        OnPropertyChanged(nameof(FileNamePreview));
        OnPropertyChanged(nameof(HasSubtitles));
        UpdateKindDetails();
        RefreshState();
    }

    private void UpdateKindDetails()
    {
        string videoQuality = VideoQuality.Selected?.Label ?? "Best";
        Kind.Items[0].Detail = CanExportVideo ? $"{VideoFormat.Selected?.Label} · {videoQuality}" : "Not available for this one";
        Kind.Items[1].Detail = $"{AudioFormat.Selected?.Label} · {AudioQuality.Selected?.Label}";
    }

    private void RefreshState()
    {
        (State, StatusText) = Evaluate();
        OnPropertyChanged(nameof(ExportLabel));
        OnPropertyChanged(nameof(ProgressFraction));
        OnPropertyChanged(nameof(IsIndeterminate));
        OnPropertyChanged(nameof(JobStatus));
    }

    private (ExportState, string?) Evaluate()
    {
        if (Media is null)
            return (ExportState.Empty, null);

        // The button shows the progress; the line under it says where it's going.
        string destination = $"Saves to {FolderName(OutputFolder)}";
        if (Job is { IsRunning: true })
            return (ExportState.Running, destination.Replace("Saves", "Saving"));

        if (Details is { IsExportable: false } unavailable)
            return (ExportState.Blocked, unavailable.Problem);

        if (Job is { } finished && _jobOptionsVersion == _optionsVersion)
        {
            if (finished.IsCompleted)
                return (ExportState.Done, finished.Files.Count > 1 ? finished.Status : $"Saved as {Path.GetFileName(finished.OutputPath)}");
            if (finished.IsFailed)
                return (ExportState.Failed, finished.Status);
        }

        if (Details is null)
            return (ExportState.Checking, "Checking what's available");

        return (ExportState.Ready, EstimateSize() is long bytes ? $"About {FormatSize(bytes)} · saves to {FolderName(OutputFolder)}" : destination);
    }

    private static string FolderName(string folder) =>
        Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } name ? name : folder;

    private long? EstimateSize()
    {
        long? full;
        if (IsAudio)
            full = ParseNumber(AudioQuality.SelectedKey) is int kbps && Duration > TimeSpan.Zero ? (long)(kbps * 125 * Duration.TotalSeconds) : Details?.AudioBytes;
        else
            full = (Details?.VideoQualities.FirstOrDefault(quality => quality.Height.ToString(CultureInfo.InvariantCulture) == VideoQuality.SelectedKey)
                ?? Details?.VideoQualities.FirstOrDefault())?.ApproximateBytes;

        if (full is not long size || Clips.Count == 0 || Duration <= TimeSpan.Zero)
            return full;

        return (long)(size * Clips.Sum(clip => clip.Length.TotalSeconds) / Duration.TotalSeconds);
    }

    private static int? ParseNumber(string? text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) ? number : null;

    internal static string Format(TimeSpan time) =>
        time.ToString(time.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture);

    internal static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0} MB",
        _ => $"{Math.Max(1, bytes / 1024)} KB"
    };
}
