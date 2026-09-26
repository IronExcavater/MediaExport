// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaExport.Core.Exporting;
using MediaExport.Core.Media.Models;
using MediaExport.Core.Media.Providers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MediaExport.Export;

public sealed record FormatChoice(string Label, string Extension);

public sealed record QualityChoice(string Label, int? MaxHeight);

public partial class ExportViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan SearchDebounceDelay = TimeSpan.FromMilliseconds(250);

    private readonly IMediaDownloader _downloader;
    private CancellationTokenSource? _searchCancellation;
    private CancellationTokenSource? _previewCancellation;

    public ExportViewModel(IEnumerable<IMediaSearchProvider> searchProviders, IMediaDownloader downloader)
    {
        _downloader = downloader;
        Providers = searchProviders.ToArray();
        SelectedProvider = Providers.FirstOrDefault();
        SelectedVideoFormat = VideoFormats[0];
        SelectedAudioFormat = AudioFormats[0];
        SelectedQuality = Qualities[3];
    }

    public void Dispose()
    {
        CancelSearch();
        _previewCancellation?.Cancel();
    }

    public IReadOnlyList<IMediaSearchProvider> Providers { get; }
    public ObservableCollection<MediaSearchResult> Suggestions { get; } = [];

    public IReadOnlyList<FormatChoice> VideoFormats { get; } =
        [new("MP4", "mp4"), new("MKV", "mkv"), new("WebM", "webm")];

    public IReadOnlyList<FormatChoice> AudioFormats { get; } =
        [new("MP3", "mp3"), new("M4A", "m4a"), new("Opus", "opus"), new("WAV", "wav")];

    public IReadOnlyList<QualityChoice> Qualities { get; } =
        [new("Best available", null), new("2160p (4K)", 2160), new("1440p", 1440), new("1080p", 1080), new("720p", 720), new("480p", 480)];

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
    public partial bool IsSuggestionListOpen { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedResult))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    public partial MediaSearchResult? SelectedResult { get; set; }

    [ObservableProperty]
    public partial Uri? AudioPreviewUri { get; set; }

    [ObservableProperty]
    public partial bool IsPreviewLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVideoExport))]
    public partial bool IsAudioExport { get; set; }

    [ObservableProperty]
    public partial FormatChoice SelectedVideoFormat { get; set; }

    [ObservableProperty]
    public partial FormatChoice SelectedAudioFormat { get; set; }

    [ObservableProperty]
    public partial QualityChoice SelectedQuality { get; set; }

    [ObservableProperty]
    public partial string OutputFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedJob))]
    public partial ExportJob? SelectedJob { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveExports))]
    public partial int ActiveExportCount { get; set; }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasSelectedResult => SelectedResult is not null;
    public bool HasSelectedJob => SelectedJob is not null;
    public bool HasActiveExports => ActiveExportCount > 0;
    public string SearchPlaceholder => $"Search {SelectedProvider?.DisplayName}";

    public bool IsVideoExport
    {
        get => !IsAudioExport;
        set => IsAudioExport = !value;
    }

    public void ShowSuggestions() =>
        IsSuggestionListOpen = !IsSearching && Suggestions.Count > 0;

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
        if (!string.IsNullOrWhiteSpace(SearchText))
            SearchCommand.Execute(null);
    }

    partial void OnSelectedResultChanged(MediaSearchResult? value)
    {
        SelectedJob = value is null ? null : Jobs.FirstOrDefault(job => job.Media.SourceUrl == value.SourceUrl);
        if (value is not null)
            IsAudioExport = value.Kind == MediaKind.Audio;

        _ = LoadPreviewAsync(value);
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SearchAsync()
    {
        if (SelectedProvider is null)
            return;

        string query = SearchText.Trim();
        if (query.Length == 0)
            return;

        CancellationTokenSource searchCancellation = StartSearch();
        CancellationToken token = searchCancellation.Token;

        IsSearching = true;
        IsSuggestionListOpen = false;
        ErrorMessage = null;

        try
        {
            await Task.Delay(SearchDebounceDelay, token);
            IReadOnlyList<MediaSearchResult> results = await SelectedProvider.SearchAsync(query, token);

            if (token.IsCancellationRequested)
                return;

            Suggestions.Clear();
            foreach (MediaSearchResult result in results)
                Suggestions.Add(result);

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
    }

    // ponytail: exports run side by side with no queue; add one if people start many at once.
    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(HasSelectedResult))]
    private async Task ExportAsync()
    {
        if (SelectedResult is not { } media)
            return;

        var options = new ExportOptions(
            IsAudioExport ? MediaKind.Audio : MediaKind.Video,
            IsAudioExport ? SelectedAudioFormat.Extension : SelectedVideoFormat.Extension,
            IsAudioExport ? null : SelectedQuality.MaxHeight,
            OutputFolder);

        var job = new ExportJob(media, options);
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

    private async Task LoadPreviewAsync(MediaSearchResult? media)
    {
        _previewCancellation?.Cancel();
        AudioPreviewUri = null;
        IsPreviewLoading = false;

        if (media?.Kind != MediaKind.Audio)
            return;

        var previewCancellation = new CancellationTokenSource();
        _previewCancellation = previewCancellation;
        IsPreviewLoading = true;
        try
        {
            Uri? stream = await _downloader.GetAudioStreamAsync(media.SourceUrl, previewCancellation.Token);
            if (!previewCancellation.IsCancellationRequested)
                AudioPreviewUri = stream;
        }
        catch (Exception)
        {
            // The preview is a convenience; the view shows it as unavailable and export still works.
        }
        finally
        {
            if (ReferenceEquals(_previewCancellation, previewCancellation))
                IsPreviewLoading = false;
        }
    }

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
