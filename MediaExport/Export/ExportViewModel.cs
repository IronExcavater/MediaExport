// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

public sealed record FormatChoice(string Label, string Extension);

public sealed record BitrateChoice(string Label, int? Kbps);

public sealed record FileNameChoice(string Label, string Template);

/// <summary>A search result, or a result picked before (<see cref="IsRecent"/>).</summary>
public sealed record SearchSuggestion(MediaSearchResult Media, bool IsRecent)
{
    // Suggestions are announced by screen readers using ToString.
    public override string ToString() => IsRecent ? $"Recent: {Media}" : Media.ToString();
}

public sealed partial class QualityChoice(string label, int? maxHeight) : ObservableObject
{
    public string Label { get; } = label;
    public int? MaxHeight { get; } = maxHeight;

    /// <summary>False when the media has no stream this tall.</summary>
    [ObservableProperty]
    public partial bool IsAvailable { get; set; } = true;
}

/// <summary>The one line of status beside the export button.</summary>
public enum ExportHintKind
{
    None,
    Checking,
    Warning,
    Error,
    Success
}

public partial class ExportViewModel : ObservableObject, IDisposable
{
    private const int RecentLimit = 12;
    private static readonly TimeSpan SearchDebounceDelay = TimeSpan.FromMilliseconds(250);
    private static readonly string[] TimeFormats = [@"%s", @"m\:ss", @"mm\:ss", @"h\:mm\:ss", @"hh\:mm\:ss"];

    // Raising these re-evaluates the export button, so they must not trigger another round.
    private static readonly HashSet<string> ExportStatusProperties =
    [
        nameof(CurrentJob), nameof(IsExporting), nameof(IsExportDone), nameof(IsExportFailed), nameof(CanStartExport),
        nameof(ExportLabel), nameof(ExportGlyph), nameof(HintKind), nameof(Hint), nameof(HasHint),
        nameof(IsHintChecking), nameof(IsHintWarning), nameof(IsHintError), nameof(IsHintSuccess)
    ];

    private readonly IMediaDownloader _downloader;
    private readonly SettingsStore _settings;
    private readonly Dictionary<string, MediaInspection> _inspections = [];
    private CancellationTokenSource? _searchCancellation;
    private CancellationTokenSource? _inspectionCancellation;
    private bool _isApplyingSettings;

    public ExportViewModel(IEnumerable<IMediaSearchProvider> searchProviders, IMediaDownloader downloader, SettingsStore? settings = null)
    {
        _downloader = downloader;
        _settings = settings ?? new SettingsStore(null);
        Providers = searchProviders.ToArray();
        ApplySettings();
        Jobs.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasJobs));
            OnPropertyChanged(nameof(ActivitySummary));
        };
    }

    public void Dispose()
    {
        CancelSearch();
        _inspectionCancellation?.Cancel();
    }

    public IReadOnlyList<IMediaSearchProvider> Providers { get; }
    public ObservableCollection<SearchSuggestion> Suggestions { get; } = [];

    public IReadOnlyList<FormatChoice> VideoFormats { get; } =
        [new("MP4", "mp4"), new("MKV", "mkv"), new("WebM", "webm")];

    public IReadOnlyList<FormatChoice> AudioFormats { get; } =
        [new("MP3", "mp3"), new("M4A", "m4a"), new("Opus", "opus"), new("WAV", "wav")];

    public IReadOnlyList<QualityChoice> Qualities { get; } =
        [new("Best available", null), new("2160p (4K)", 2160), new("1440p", 1440), new("1080p", 1080), new("720p", 720), new("480p", 480)];

    public IReadOnlyList<BitrateChoice> AudioBitrates { get; } =
        [new("Best available", null), new("320 kbps", 320), new("256 kbps", 256), new("192 kbps", 192), new("128 kbps", 128)];

    public IReadOnlyList<FileNameChoice> FileNames { get; } =
        [new("Title", "%(title)s"), new("Creator - Title", "%(uploader)s - %(title)s"), new("Title [ID]", "%(title)s [%(id)s]")];

    /// <summary>Every export started this session, newest first.</summary>
    public ObservableCollection<ExportJob> Jobs { get; } = [];

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SearchPlaceholder))]
    public partial IMediaSearchProvider? SelectedProvider { get; set; }

    [ObservableProperty]
    public partial bool IsSearching { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlayerObscured))]
    public partial bool IsSuggestionListOpen { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlayerObscured))]
    public partial bool IsSourceMenuOpen { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedResult), nameof(FileNamePreview), nameof(TrimEndPlaceholder))]
    public partial MediaSearchResult? SelectedResult { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnavailable), nameof(UnavailableReason), nameof(CanExportVideo))]
    public partial MediaInspection? Inspection { get; set; }

    [ObservableProperty]
    public partial bool IsInspecting { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVideoExport), nameof(FileNamePreview), nameof(TrimSummary))]
    public partial bool IsAudioExport { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileNamePreview))]
    public partial FormatChoice SelectedVideoFormat { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileNamePreview))]
    public partial FormatChoice SelectedAudioFormat { get; set; }

    [ObservableProperty]
    public partial QualityChoice SelectedQuality { get; set; }

    [ObservableProperty]
    public partial BitrateChoice SelectedAudioBitrate { get; set; }

    [ObservableProperty]
    public partial string OutputFolder { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MetadataSummary))]
    public partial bool EmbedMetadata { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MetadataSummary))]
    public partial bool EmbedThumbnail { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MetadataSummary))]
    public partial bool EmbedChapters { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubtitlesSummary))]
    public partial bool DownloadSubtitles { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubtitlesSummary))]
    public partial string SubtitleLanguages { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubtitlesSummary))]
    public partial bool IncludeAutomaticSubtitles { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TrimSummary), nameof(TrimError))]
    public partial string TrimStart { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TrimSummary), nameof(TrimError))]
    public partial string TrimEnd { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileNamePreview))]
    public partial FileNameChoice SelectedFileName { get; set; }

    /// <summary>The latest export of the selected media.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedJob))]
    public partial ExportJob? SelectedJob { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveExports), nameof(ActivitySummary))]
    public partial int ActiveExportCount { get; set; }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasSelectedResult => SelectedResult is not null;
    public bool HasSelectedJob => SelectedJob is not null;
    public bool HasActiveExports => ActiveExportCount > 0;
    public bool HasJobs => Jobs.Count > 0;
    public bool HasRecentResults => _settings.Current.RecentResults.Count > 0;
    public string SearchPlaceholder => $"Search {SelectedProvider?.DisplayName}";

    public string ActivitySummary =>
        $"{Jobs.Count} {(Jobs.Count == 1 ? "export" : "exports")}"
        + (ActiveExportCount > 0 ? $" · {ActiveExportCount} running" : string.Empty);

    /// <summary>Web players swallow input meant for popups drawn over them, so they step aside.</summary>
    public bool IsPlayerObscured => IsSuggestionListOpen || IsSourceMenuOpen;

    public bool IsUnavailable => Inspection is { IsExportable: false };
    public string? UnavailableReason => IsUnavailable ? Inspection!.Problem : null;
    public bool CanExportVideo => Inspection?.HasVideo ?? SelectedResult?.Kind != MediaKind.Audio;

    public bool IsVideoExport
    {
        get => !IsAudioExport;
        set => IsAudioExport = !value;
    }

    // The export button: Export, then progress (click to cancel), then the file, or back to Export on failure.

    /// <summary>The selected media's export while it runs, or while its result still matches the chosen options.</summary>
    public ExportJob? CurrentJob =>
        SelectedJob is { } job && (job.IsRunning || job.Options == CreateOptions()) ? job : null;

    public bool IsExporting => CurrentJob?.IsRunning == true;
    public bool IsExportDone => CurrentJob?.IsCompleted == true;
    public bool IsExportFailed => CurrentJob?.IsFailed == true;
    public bool CanStartExport => !IsExporting && !IsExportDone;
    public string ExportLabel => IsExportFailed ? "Try again" : "Export";
    public string ExportGlyph => IsExportFailed ? "" : "";

    public ExportHintKind HintKind => this switch
    {
        { IsUnavailable: true } or { TrimError: not null } => ExportHintKind.Warning,
        { IsExportFailed: true } => ExportHintKind.Error,
        { IsExportDone: true } => ExportHintKind.Success,
        { IsInspecting: true, IsExporting: false } => ExportHintKind.Checking,
        _ => ExportHintKind.None
    };

    public string? Hint => HintKind switch
    {
        ExportHintKind.Warning => UnavailableReason ?? TrimError,
        ExportHintKind.Error => CurrentJob!.Status,
        ExportHintKind.Success => $"{CurrentJob!.Status} as {Path.GetFileName(CurrentJob.OutputPath)}",
        ExportHintKind.Checking => "Checking what's available",
        _ => null
    };

    public bool HasHint => HintKind != ExportHintKind.None;
    public bool IsHintChecking => HintKind == ExportHintKind.Checking;
    public bool IsHintWarning => HintKind == ExportHintKind.Warning;
    public bool IsHintError => HintKind == ExportHintKind.Error;
    public bool IsHintSuccess => HintKind == ExportHintKind.Success;

    public string MetadataSummary =>
        new[] { EmbedMetadata ? "Details" : null, EmbedThumbnail ? "cover art" : null, EmbedChapters ? "chapters" : null }
            .OfType<string>().ToArray() is { Length: > 0 } parts
            ? Capitalize(string.Join(", ", parts)) + " embedded"
            : "Nothing embedded";

    public string SubtitlesSummary => DownloadSubtitles
        ? $"{SubtitleLanguages.Trim()}{(IncludeAutomaticSubtitles ? ", including automatic" : string.Empty)}, embedded"
        : "Off";

    public string TrimSummary => (ParseTime(TrimStart), ParseTime(TrimEnd)) switch
    {
        _ when TrimError is not null => TrimError,
        (null, null) => "Full length",
        (var start, var end) => $"{Format(start ?? TimeSpan.Zero)} – {(end is { } value ? Format(value) : "end")}"
    };

    public string TrimEndPlaceholder => SelectedResult?.Duration is { } duration ? Format(duration) : "End";

    public string? TrimError
    {
        get
        {
            if (TrimStart.Length > 0 && ParseTime(TrimStart) is null || TrimEnd.Length > 0 && ParseTime(TrimEnd) is null)
                return "Use times like 1:30";

            return ParseTime(TrimStart) is { } start && ParseTime(TrimEnd) is { } end && end <= start
                ? "The end must come after the start"
                : null;
        }
    }

    public string FileNamePreview
    {
        get
        {
            string name = SelectedFileName.Template
                .Replace("%(title)s", SelectedResult?.Title ?? "Title")
                .Replace("%(uploader)s", SelectedResult?.Creator ?? "Creator")
                .Replace("%(id)s", SelectedResult?.Identity ?? "id");
            return $"{name}.{(IsAudioExport ? SelectedAudioFormat : SelectedVideoFormat).Extension}";
        }
    }

    /// <summary>Opens the suggestions on focus: recent results when there's no text yet.</summary>
    public void ShowSuggestions()
    {
        if (IsSearching)
            return;

        if (SearchText.Trim().Length == 0)
            ShowSuggestions([], string.Empty);

        IsSuggestionListOpen = Suggestions.Count > 0;
    }

    public void ClearRecentResults()
    {
        _settings.Current.RecentResults.Clear();
        _settings.Save();
        foreach (SearchSuggestion recent in Suggestions.Where(suggestion => suggestion.IsRecent).ToArray())
            Suggestions.Remove(recent);
        OnPropertyChanged(nameof(HasRecentResults));
    }

    /// <summary>Removes finished exports, and cancels then removes running ones.</summary>
    public void Remove(IEnumerable<ExportJob> jobs)
    {
        foreach (ExportJob job in jobs.ToArray())
        {
            job.CancelCommand.Execute(null);
            Jobs.Remove(job);
            if (SelectedJob == job)
                SelectedJob = null;
        }
    }

    [RelayCommand]
    private void ClearFinished() => Remove(Jobs.Where(job => !job.IsRunning));

    [RelayCommand]
    private void CancelExport() => CurrentJob?.CancelCommand.Execute(null);

    // Leaves the list open: choosing a suggestion changes the text, and closing would drop the choice.
    partial void OnSearchTextChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            return;

        CancelSearch();
        Suggestions.Clear();
        ErrorMessage = null;
        SelectedResult = null;
        IsSearching = false;
    }

    partial void OnSelectedProviderChanged(IMediaSearchProvider? value)
    {
        Suggestions.Clear();
        if (!_isApplyingSettings && !string.IsNullOrWhiteSpace(SearchText))
            SearchCommand.Execute(null);
    }

    partial void OnSelectedResultChanged(MediaSearchResult? value)
    {
        SelectedJob = value is null ? null : Jobs.FirstOrDefault(job => job.Media.SourceUrl == value.SourceUrl);
        if (value is not null)
            IsAudioExport = value.Kind == MediaKind.Audio;

        _ = InspectAsync(value);
    }

    partial void OnSelectedJobChanged(ExportJob? oldValue, ExportJob? newValue)
    {
        if (oldValue is not null)
            oldValue.PropertyChanged -= OnSelectedJobPropertyChanged;
        if (newValue is not null)
            newValue.PropertyChanged += OnSelectedJobPropertyChanged;
    }

    private void OnSelectedJobPropertyChanged(object? sender, PropertyChangedEventArgs args) => RaiseExportStatus();

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SearchAsync()
    {
        if (SelectedProvider is null)
            return;

        string query = SearchText.Trim();
        if (query.Length == 0)
        {
            ShowSuggestions();
            return;
        }

        CancellationTokenSource searchCancellation = StartSearch();
        CancellationToken token = searchCancellation.Token;

        IsSearching = true;
        ErrorMessage = null;

        // Recent results that match show straight away; the search fills in below them.
        ShowSuggestions([], query);
        IsSuggestionListOpen = Suggestions.Count > 0;

        try
        {
            await Task.Delay(SearchDebounceDelay, token);
            IReadOnlyList<MediaSearchResult> results = await SelectedProvider.SearchAsync(query, token);

            if (token.IsCancellationRequested)
                return;

            ShowSuggestions(results, query);
            IsSuggestionListOpen = Suggestions.Count > 0;
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (!token.IsCancellationRequested)
        {
            ErrorMessage = exception.Message;
            IsSuggestionListOpen = false;
        }
        finally
        {
            if (ReferenceEquals(_searchCancellation, searchCancellation))
                IsSearching = false;
        }
    }

    [RelayCommand]
    private void SelectResult(MediaSearchResult result)
    {
        CancelSearch();
        IsSearching = false;
        SelectedResult = result;
        IsSuggestionListOpen = false;

        List<MediaSearchResult> recents = _settings.Current.RecentResults;
        recents.RemoveAll(recent => recent.SourceUrl == result.SourceUrl);
        recents.Insert(0, result);
        if (recents.Count > RecentLimit)
            recents.RemoveRange(RecentLimit, recents.Count - RecentLimit);
        _settings.Save();
        OnPropertyChanged(nameof(HasRecentResults));
    }

    private bool CanExport() => HasSelectedResult && !IsUnavailable && TrimError is null && CanStartExport;

    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanExport))]
    private Task ExportAsync() => RunExportAsync();

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task ExportAgainAsync() => RunExportAsync();

    // ponytail: exports run side by side with no queue; add one if people start many at once.
    private async Task RunExportAsync()
    {
        if (SelectedResult is not { } media)
            return;

        var job = new ExportJob(media, CreateOptions());
        Jobs.Insert(0, job);
        SelectedJob = job;
        ActiveExportCount++;
        try
        {
            await job.RunAsync(_downloader);
        }
        finally
        {
            ActiveExportCount--;
        }
    }

    private ExportOptions CreateOptions() =>
        new(
            IsAudioExport ? MediaKind.Audio : MediaKind.Video,
            IsAudioExport ? SelectedAudioFormat.Extension : SelectedVideoFormat.Extension,
            IsAudioExport ? null : SelectedQuality.MaxHeight,
            OutputFolder)
        {
            AudioBitrate = IsAudioExport ? SelectedAudioBitrate.Kbps : null,
            EmbedMetadata = EmbedMetadata,
            EmbedThumbnail = EmbedThumbnail,
            EmbedChapters = EmbedChapters,
            SubtitleLanguages = !IsAudioExport && DownloadSubtitles && SubtitleLanguages.Trim().Length > 0
                ? SubtitleLanguages.Replace(" ", string.Empty)
                : null,
            IncludeAutomaticSubtitles = IncludeAutomaticSubtitles,
            TrimStart = ParseTime(TrimStart),
            TrimEnd = ParseTime(TrimEnd),
            FileNameTemplate = SelectedFileName.Template
        };

    /// <summary>Recent results matching the query (all of them when it's empty), then the search results.</summary>
    private void ShowSuggestions(IReadOnlyList<MediaSearchResult> results, string query)
    {
        string[] words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        SearchSuggestion[] recents = _settings.Current.RecentResults
            .Where(recent => words.All(word => $"{recent.Title} {recent.Creator}".Contains(word, StringComparison.OrdinalIgnoreCase)))
            .Take(words.Length == 0 ? RecentLimit : 3)
            .Select(recent => new SearchSuggestion(recent, true))
            .ToArray();

        Suggestions.Clear();
        foreach (SearchSuggestion recent in recents)
            Suggestions.Add(recent);
        foreach (MediaSearchResult result in results.Where(result => recents.All(recent => recent.Media.SourceUrl != result.SourceUrl)))
            Suggestions.Add(new SearchSuggestion(result, false));
    }

    private async Task InspectAsync(MediaSearchResult? media)
    {
        _inspectionCancellation?.Cancel();
        Inspection = null;
        IsInspecting = false;
        foreach (QualityChoice quality in Qualities)
            quality.IsAvailable = true;

        if (media is null)
            return;

        if (_inspections.TryGetValue(media.SourceUrl, out MediaInspection? known))
        {
            ApplyInspection(known);
            return;
        }

        var inspectionCancellation = new CancellationTokenSource();
        _inspectionCancellation = inspectionCancellation;
        IsInspecting = true;
        try
        {
            MediaInspection inspection = await _downloader.InspectAsync(media.SourceUrl, inspectionCancellation.Token);
            _inspections[media.SourceUrl] = inspection;
            if (!inspectionCancellation.IsCancellationRequested)
                ApplyInspection(inspection);
        }
        catch (Exception)
        {
            // Inspection only refines the options; an export still reports its own errors.
        }
        finally
        {
            if (ReferenceEquals(_inspectionCancellation, inspectionCancellation))
                IsInspecting = false;
        }
    }

    private void ApplyInspection(MediaInspection inspection)
    {
        Inspection = inspection;
        if (!inspection.HasVideo)
            IsAudioExport = true;

        foreach (QualityChoice quality in Qualities)
            quality.IsAvailable = quality.MaxHeight is null || inspection.MaxHeight is null || quality.MaxHeight <= inspection.MaxHeight;

        // The tallest height it does have, or "Best available" when it's shorter than every preset.
        if (!SelectedQuality.IsAvailable)
            SelectedQuality = Qualities.FirstOrDefault(quality => quality.IsAvailable && quality.MaxHeight is not null) ?? Qualities[0];
    }

    private void ApplySettings()
    {
        AppSettings settings = _settings.Current;
        _isApplyingSettings = true;
        SelectedProvider = Providers.FirstOrDefault(provider => provider.Id == settings.ProviderId) ?? Providers.FirstOrDefault();
        SelectedVideoFormat = VideoFormats.FirstOrDefault(format => format.Extension == settings.VideoFormat) ?? VideoFormats[0];
        SelectedAudioFormat = AudioFormats.FirstOrDefault(format => format.Extension == settings.AudioFormat) ?? AudioFormats[0];
        SelectedQuality = Qualities.FirstOrDefault(quality => quality.MaxHeight == settings.VideoQuality) ?? Qualities[0];
        SelectedAudioBitrate = AudioBitrates.FirstOrDefault(bitrate => bitrate.Kbps == settings.AudioBitrate) ?? AudioBitrates[0];
        SelectedFileName = FileNames.FirstOrDefault(name => name.Template == settings.FileNameTemplate) ?? FileNames[0];
        OutputFolder = settings.OutputFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        EmbedMetadata = settings.EmbedMetadata;
        EmbedThumbnail = settings.EmbedThumbnail;
        EmbedChapters = settings.EmbedChapters;
        SubtitleLanguages = settings.SubtitleLanguages;
        _isApplyingSettings = false;
    }

    // Choices people rarely change per export are remembered; everything else re-evaluates the export button.
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is null || ExportStatusProperties.Contains(e.PropertyName))
            return;

        if (!_isApplyingSettings && Remember(e.PropertyName))
            _settings.Save();

        RaiseExportStatus();
    }

    private bool Remember(string propertyName)
    {
        AppSettings settings = _settings.Current;
        switch (propertyName)
        {
            case nameof(SelectedProvider): settings.ProviderId = SelectedProvider?.Id; break;
            case nameof(SelectedVideoFormat): settings.VideoFormat = SelectedVideoFormat.Extension; break;
            case nameof(SelectedAudioFormat): settings.AudioFormat = SelectedAudioFormat.Extension; break;
            case nameof(SelectedQuality): settings.VideoQuality = SelectedQuality.MaxHeight; break;
            case nameof(SelectedAudioBitrate): settings.AudioBitrate = SelectedAudioBitrate.Kbps; break;
            case nameof(SelectedFileName): settings.FileNameTemplate = SelectedFileName.Template; break;
            case nameof(OutputFolder): settings.OutputFolder = OutputFolder; break;
            case nameof(EmbedMetadata): settings.EmbedMetadata = EmbedMetadata; break;
            case nameof(EmbedThumbnail): settings.EmbedThumbnail = EmbedThumbnail; break;
            case nameof(EmbedChapters): settings.EmbedChapters = EmbedChapters; break;
            case nameof(SubtitleLanguages): settings.SubtitleLanguages = SubtitleLanguages; break;
            default: return false;
        }

        return true;
    }

    private void RaiseExportStatus()
    {
        foreach (string property in ExportStatusProperties)
            OnPropertyChanged(property);
        ExportCommand.NotifyCanExecuteChanged();
    }

    internal static TimeSpan? ParseTime(string text) =>
        TimeSpan.TryParseExact(text.Trim(), TimeFormats, CultureInfo.InvariantCulture, out TimeSpan time) ? time : null;

    private static string Format(TimeSpan time) =>
        time.ToString(time.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture);

    private static string Capitalize(string text) => char.ToUpperInvariant(text[0]) + text[1..];

    private CancellationTokenSource StartSearch()
    {
        CancelSearch();

        var searchCancellation = new CancellationTokenSource();
        _searchCancellation = searchCancellation;
        return searchCancellation;
    }

    private void CancelSearch()
    {
        CancellationTokenSource? searchCancellation = _searchCancellation;
        _searchCancellation = null;

        if (searchCancellation is null)
            return;

        searchCancellation.Cancel();
        searchCancellation.Dispose();
    }
}
