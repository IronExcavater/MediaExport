// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaExport.Activity;
using MediaExport.Core.Exporting;
using MediaExport.Core.Media.Models;
using MediaExport.Core.Media.Providers;
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

/// <summary>A clip as listed under the timeline: its number, its times, and whether it's the one being edited.</summary>
public sealed record ClipItem(int Index, Clip Clip, bool IsSelected)
{
    public int Number => Index + 1;
    public string Range => $"{ExportViewModel.Format(Clip.Start)} – {ExportViewModel.Format(Clip.End)}";
    public override string ToString() => $"Clip {Number}, {Range}";
}

/// <summary>The media being exported and every choice about how.</summary>
public sealed partial class ExportViewModel : ObservableObject
{
    private const string Best = "best";

    private readonly IMediaDownloader _downloader;
    private readonly ActivityViewModel _activity;
    private readonly SettingsStore _settings;
    private readonly Dictionary<string, MediaDetails> _details = [];
    private readonly Dictionary<string, string> _sourceNames;
    private CancellationTokenSource? _inspection;
    private bool _isRestoring;

    // A finished job counts as "done" only until a choice changes.
    private int _optionsVersion;
    private int _jobOptionsVersion;

    public ExportViewModel(IMediaDownloader downloader, ActivityViewModel activity, SettingsStore settings, IEnumerable<IMediaSearchProvider> providers)
    {
        _downloader = downloader;
        _sourceNames = providers.ToDictionary(provider => provider.Id, provider => provider.DisplayName);
        _activity = activity;
        _settings = settings;
        AppSettings saved = settings.Current;
        _isRestoring = true; // Otherwise each choice restored here would save the rest before they're restored.

        VideoFormat = new ChoiceGroup(
            [new Choice("mp4", "MP4", "Plays everywhere"), new Choice("mkv", "MKV", "Keeps every track"), new Choice("webm", "WebM", "Open, often smaller")],
            saved.VideoFormat);
        AudioFormat = new ChoiceGroup(
            [new Choice("mp3", "MP3", "Plays everywhere"), new Choice("m4a", "M4A", "Apple devices"), new Choice("opus", "Opus", "Smallest"),
             new Choice("flac", "FLAC", "Lossless"), new Choice("wav", "WAV", "Uncompressed")],
            saved.AudioFormat);
        VideoQuality = new ChoiceGroup(VideoQualities(null), Key(saved.VideoQuality));
        AudioQuality = new ChoiceGroup(
            [new Choice(Best, "Best"), new Choice("320", "320 kbps"), new Choice("256", "256 kbps"), new Choice("192", "192 kbps"), new Choice("128", "128 kbps")],
            Key(saved.AudioBitrate));
        FileName = new ChoiceGroup(
            [new Choice("%(title)s", "Title"), new Choice("%(uploader)s - %(title)s", "Creator - Title"), new Choice("%(title)s [%(id)s]", "Title [ID]")],
            saved.FileNameTemplate);

        IncludeVideo = saved.IncludeVideo;
        IncludeAudio = saved.IncludeAudio || !saved.IncludeVideo;
        OutputFolder = saved.OutputFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        EmbedDetails = saved.EmbedMetadata && saved.EmbedThumbnail;
        EmbedChapters = saved.EmbedChapters;
        RemoveSponsors = saved.RemoveSponsors;
        _isRestoring = false;

        foreach (ChoiceGroup group in new[] { VideoFormat, AudioFormat, VideoQuality, AudioQuality, FileName })
            group.Changed += (_, _) => OnOptionChanged();
        Clips.CollectionChanged += (_, _) =>
        {
            if (SelectedClip >= Clips.Count)
                SelectedClip = Clips.Count - 1;
            OnOptionChanged();
        };
        activity.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ActivityViewModel.RunningCount))
                RefreshState();
        };
    }

    public ChoiceGroup VideoFormat { get; }
    public ChoiceGroup AudioFormat { get; }
    public ChoiceGroup VideoQuality { get; }
    public ChoiceGroup AudioQuality { get; }
    public ChoiceGroup FileName { get; }

    public ObservableCollection<SubtitleChoice> Subtitles { get; } = [];

    /// <summary>Parts to keep, in time order; empty keeps all of it.</summary>
    public ObservableCollection<Clip> Clips { get; } = [];

    /// <summary>The clips as listed under the timeline.</summary>
    public ObservableCollection<ClipItem> ClipItems { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMedia), nameof(Duration), nameof(HasCreatorLink), nameof(ProviderName))]
    public partial MediaSearchResult? Media { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Chapters), nameof(HasChapters), nameof(CanRemoveSponsors), nameof(HasVideo))]
    public partial MediaDetails? Details { get; private set; }

    /// <summary>Whether the file has pictures. Without, it's an audio file.</summary>
    [ObservableProperty]
    public partial bool IncludeVideo { get; set; } = true;

    [ObservableProperty]
    public partial bool IncludeAudio { get; set; } = true;

    [ObservableProperty]
    public partial string OutputFolder { get; set; }

    [ObservableProperty]
    public partial bool EmbedDetails { get; set; }

    [ObservableProperty]
    public partial bool EmbedChapters { get; set; }

    [ObservableProperty]
    public partial bool RemoveSponsors { get; set; }

    [ObservableProperty]
    public partial bool JoinClips { get; set; } = true;

    /// <summary>The clip being edited, or -1.</summary>
    [ObservableProperty]
    public partial int SelectedClip { get; set; } = -1;

    /// <summary>The export shown on the button: running, or finished with the current options.</summary>
    [ObservableProperty]
    public partial ExportJob? Job { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning), nameof(IsDone), nameof(IsFailed), nameof(IsBlocked), nameof(CanStart), nameof(IsBusy), nameof(ExportGlyph))]
    public partial ExportState State { get; private set; }

    [ObservableProperty]
    public partial string? StatusText { get; private set; }

    public bool HasMedia => Media is not null;

    /// <summary>The source's name, e.g. "YouTube"; for a pasted link from elsewhere, its site's id.</summary>
    public string? ProviderName => Media?.ProviderId is { } id ? _sourceNames.GetValueOrDefault(id, id) : null;
    public bool HasCreatorLink => Media?.CreatorUrl is not null;

    /// <summary>Whether the media has pictures at all; sound-only media can't be exported as video.</summary>
    public bool HasVideo => Details?.HasVideo ?? Media?.Kind != MediaKind.Audio;
    public bool HasChapters => Chapters.Count > 0;
    public bool HasSubtitles => Subtitles.Count > 0;
    public bool CanRemoveSponsors => Details?.SupportsSponsorBlock == true;
    public bool HasClips => Clips.Count > 0;
    public bool HasSeveralClips => Clips.Count > 1;
    public bool IsAudioOnly => !IncludeVideo;
    public bool CanChooseSubtitles => IncludeVideo && HasSubtitles;
    public bool CanEmbedChapters => HasChapters && !HasClips;
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
        _ => IncludeVideo ? "Export video" : "Export audio"
    };

    /// <summary>With video, the sound goes in the video's container in its usual codec.</summary>
    public string AudioCodec => VideoFormat.SelectedKey switch
    {
        "webm" => "Opus, to suit WebM",
        "mkv" => "As the site has it",
        _ => "AAC, to suit MP4"
    };

    public double ProgressFraction => Job is { } job && (IsRunning || IsFailed) ? job.DisplayProgress / 100 : 0;
    public string? JobStatus => Job?.Status;

    public string ClipsSummary => Clips.Count switch
    {
        0 => "All of it",
        1 => $"1 clip, {Format(Clips[0].Length)}",
        var count => $"{count} clips, {Format(TimeSpan.FromTicks(Clips.Sum(clip => clip.Length.Ticks)))}"
    };

    public string SubtitlesSummary => Subtitles.Where(choice => choice.IsSelected).Select(choice => choice.Track.Name).ToArray() switch
    {
        { Length: 0 } => "None",
        { Length: 1 } one => one[0],
        var several => $"{several.Length} languages"
    };

    public string FileNamePreview
    {
        get
        {
            string name = (FileName.SelectedKey ?? "%(title)s")
                .Replace("%(title)s", Media?.Title ?? "Title")
                .Replace("%(uploader)s", Media?.Creator ?? "Creator")
                .Replace("%(id)s", Media?.Identity ?? "id");
            string extension = (IncludeVideo ? VideoFormat : AudioFormat).SelectedKey ?? "mp4";
            return Clips.Count > 1 && !JoinClips ? $"{name} (clip 1).{extension}" : $"{name}.{extension}";
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
        SelectedClip = -1;
        Details = null;
        Subtitles.Clear();
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
        IncludeVideo = options.IncludeVideo;
        IncludeAudio = options.IncludeAudio;
        (options.IncludeVideo ? VideoFormat : AudioFormat).Select(options.Format);
        VideoQuality.Select(Key(options.MaxHeight));
        AudioQuality.Select(Key(options.AudioBitrate));
        foreach (Clip clip in options.Clips)
            Clips.Add(clip);
        JoinClips = options.JoinClips;
        RemoveSponsors = options.RemoveSponsors;
    }

    /// <summary>Adds a clip in time order and selects it.</summary>
    public void AddClip(Clip clip)
    {
        int index = Clips.TakeWhile(other => other.Start < clip.Start).Count();
        Clips.Insert(index, clip);
        SelectedClip = index;
    }

    /// <summary>Changes the selected clip's times, if it stays at least a second long and clear of the clips beside it.</summary>
    public bool TrySetSelectedClip(TimeSpan start, TimeSpan end)
    {
        int index = SelectedClip;
        if (index < 0 || index >= Clips.Count)
            return false;

        TimeSpan floor = index > 0 ? Clips[index - 1].End : TimeSpan.Zero;
        TimeSpan ceiling = index < Clips.Count - 1 ? Clips[index + 1].Start : Duration;
        if (end - start < TimeSpan.FromSeconds(1) || start < floor || end > ceiling)
            return false;

        Clips[index] = new Clip(start, end);
        return true;
    }

    /// <summary>A clip from <paramref name="at"/>, up to 30 seconds long, in the free space there.</summary>
    [RelayCommand]
    private void AddClipAt(TimeSpan at)
    {
        if (Duration <= TimeSpan.Zero || Clips.Any(clip => clip.Start <= at && at < clip.End))
            return;

        TimeSpan end = Clips.Where(clip => clip.Start > at).Select(clip => clip.Start).DefaultIfEmpty(Duration).Min();
        end = TimeSpan.FromTicks(Math.Min(end.Ticks, (at + TimeSpan.FromSeconds(30)).Ticks));
        if (end - at >= TimeSpan.FromSeconds(1))
            AddClip(new Clip(at, end));
    }

    /// <summary>A clip per chapter, saved as a file each.</summary>
    [RelayCommand]
    private void ClipChapters()
    {
        Clips.Clear();
        foreach (Chapter chapter in Chapters)
            Clips.Add(new Clip(chapter.Start, chapter.End));
        JoinClips = false;
        SelectedClip = -1;
    }

    [RelayCommand]
    private void RemoveClip(int index)
    {
        if (index >= 0 && index < Clips.Count)
            Clips.RemoveAt(index);
        SelectedClip = -1;
    }

    [RelayCommand]
    private void ClearClips()
    {
        Clips.Clear();
        SelectedClip = -1;
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
    private void OpenFolder() =>
        System.Diagnostics.Process.Start("explorer.exe", $"\"{OutputFolder}\"");

    internal ExportOptions CreateOptions() =>
        new((IncludeVideo ? VideoFormat : AudioFormat).SelectedKey ?? (IncludeVideo ? "mp4" : "mp3"), OutputFolder)
        {
            IncludeVideo = IncludeVideo,
            IncludeAudio = IncludeAudio || !IncludeVideo,
            MaxHeight = IncludeVideo ? ParseNumber(VideoQuality.SelectedKey) : null,
            AudioBitrate = ParseNumber(AudioQuality.SelectedKey),
            EmbedMetadata = EmbedDetails,
            EmbedThumbnail = EmbedDetails,
            EmbedChapters = EmbedChapters && CanEmbedChapters,
            SubtitleLanguages = CanChooseSubtitles ? Subtitles.Where(choice => choice.IsSelected).Select(choice => choice.Track.Language).ToArray() : [],
            Clips = Clips.ToArray(),
            JoinClips = JoinClips,
            RemoveSponsors = RemoveSponsors && CanRemoveSponsors,
            CookiesFromBrowser = SignInBrowser(_settings.Current.CookiesFromBrowser),
            SignInOnlyWhenNeeded = _settings.Current.CookiesFromBrowser?.StartsWith("auto", StringComparison.Ordinal) == true,
            FileNameTemplate = FileName.SelectedKey ?? "%(title)s"
        };

    // At least one of video and sound: switching one off with the other already off switches the other on.
    partial void OnIncludeVideoChanged(bool value)
    {
        if (!value && !IncludeAudio)
            IncludeAudio = true;
    }

    partial void OnIncludeAudioChanged(bool value)
    {
        if (!value && !IncludeVideo)
            IncludeVideo = true;
    }

    partial void OnSelectedClipChanged(int value) => SyncClipItems();

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
        if (e.PropertyName is nameof(OutputFolder) or nameof(EmbedDetails) or nameof(EmbedChapters) or nameof(RemoveSponsors)
            or nameof(JoinClips) or nameof(IncludeVideo) or nameof(IncludeAudio))
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
        saved.IncludeVideo = IncludeVideo;
        saved.IncludeAudio = IncludeAudio;
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
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            // Details only refine the choices; the export still reports its own errors.
            if (Media == media)
                ApplyDetails(new MediaDetails(null, [], null, [], [], false));
        }
    }

    private void ApplyDetails(MediaDetails details)
    {
        _isRestoring = true;
        Details = details;

        // Remembered heights the media doesn't have fall back to the tallest it does, not to "Best".
        string? wanted = Key(_settings.Current.VideoQuality);
        VideoQuality.Reset(VideoQualities(details), wanted);
        if (_settings.Current.VideoQuality is int height && details.VideoQualities.Count > 0 && VideoQuality.SelectedKey != wanted)
            VideoQuality.Select(Key(details.VideoQualities.FirstOrDefault(quality => quality.Height <= height)?.Height));

        Subtitles.Clear();
        foreach (SubtitleTrack track in details.Subtitles)
        {
            var choice = new SubtitleChoice(track) { IsSelected = _settings.Current.SubtitleLanguageCodes.Contains(track.Language) };
            choice.PropertyChanged += (_, _) => OnOptionChanged();
            Subtitles.Add(choice);
        }

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
            yield return new Choice(height.ToString(CultureInfo.InvariantCulture), height >= 2160 ? "4K" : $"{height}p", bytes is long size ? FormatSize(size) : null);
    }

    private void RefreshAll()
    {
        foreach (string property in new[]
        {
            nameof(IsAudioOnly), nameof(HasVideo), nameof(HasClips), nameof(HasSeveralClips), nameof(ClipsSummary), nameof(FileNamePreview),
            nameof(HasSubtitles), nameof(CanChooseSubtitles), nameof(SubtitlesSummary), nameof(CanEmbedChapters), nameof(AudioCodec)
        })
        {
            OnPropertyChanged(property);
        }

        // Media without pictures can only be exported as audio.
        if (Details is not null && !HasVideo && IncludeVideo)
            IncludeVideo = false;

        SyncClipItems();
        RefreshState();
    }

    private void SyncClipItems()
    {
        ClipItem[] items = Clips.Select((clip, index) => new ClipItem(index, clip, index == SelectedClip)).ToArray();
        if (items.SequenceEqual(ClipItems))
            return;

        ClipItems.Clear();
        foreach (ClipItem item in items)
            ClipItems.Add(item);
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

        if (Job is { IsRunning: true })
            return (ExportState.Running, $"Saving to {FolderName(OutputFolder)}");

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

        return (ExportState.Ready, EstimateSize() is long bytes ? $"About {FormatSize(bytes)}" : null);
    }

    private long? EstimateSize()
    {
        long? full;
        if (!IncludeVideo)
            full = ParseNumber(AudioQuality.SelectedKey) is int kbps && Duration > TimeSpan.Zero ? (long)(kbps * 125 * Duration.TotalSeconds) : Details?.AudioBytes;
        else
            full = (Details?.VideoQualities.FirstOrDefault(quality => Key(quality.Height) == VideoQuality.SelectedKey)
                ?? Details?.VideoQualities.FirstOrDefault())?.ApproximateBytes;

        if (full is not long size || Clips.Count == 0 || Duration <= TimeSpan.Zero)
            return full;

        return (long)(size * Clips.Sum(clip => clip.Length.TotalSeconds) / Duration.TotalSeconds);
    }

    // "auto:edge" signs in with Edge when needed; plain "auto" hasn't found a default browser, so there's none to use.
    private static string? SignInBrowser(string? setting) => setting switch
    {
        null or "auto" or "never" => null,
        _ when setting.StartsWith("auto:", StringComparison.Ordinal) => setting["auto:".Length..],
        _ => setting
    };

    private static string? Key(int? number) => number?.ToString(CultureInfo.InvariantCulture) ?? Best;

    private static int? ParseNumber(string? text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) ? number : null;

    private static string FolderName(string folder) =>
        Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } name ? name : folder;

    internal static string Format(TimeSpan time) =>
        time.ToString(time.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture);

    internal static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0} MB",
        _ => $"{Math.Max(1, bytes / 1024)} KB"
    };
}
