// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using MediaExport.Core.Media.Models;
using MediaExport.Core.Media.Providers;
using MediaExport.Core.Media.Ytdlp;
using MediaExport.Core.Settings;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MediaExport.Search;

/// <summary>A result or recent result, or (with no media) a line saying why there aren't any.</summary>
/// <param name="Query">What was searched; the box keeps showing it while suggestions are highlighted.</param>
/// <param name="ShowSource">Whether to say where it's from, as when every source is searched together.</param>
public sealed record SearchSuggestion(MediaSearchResult? Media, bool IsRecent = false, string? Message = null, string Query = "", bool ShowSource = false)
{
    public bool IsMessage => Media is null;

    // Suggestions are announced by screen readers using ToString.
    public override string ToString() => Message ?? (IsRecent ? $"Recent: {Media}" : $"{Media}");
}

/// <summary>The title bar search: results as you type, recent picks from the same source, and pasted links.</summary>
public sealed partial class SearchViewModel : ObservableObject
{
    public const int RecentLimit = 24;
    private const int RecentMatchLimit = 3;
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(400);

    private readonly SettingsStore _settings;
    private readonly YtdlpService _ytdlp;
    private readonly Dictionary<string, MediaDetails> _linkDetails = [];

    // Each source's last results, so switching sources back and forth doesn't search again.
    private readonly Dictionary<string, (string Query, SearchSort Sort, IReadOnlyList<MediaSearchResult> Results)> _results = [];
    private CancellationTokenSource? _search;
    private string? _shownQuery;

    public SearchViewModel(IEnumerable<IMediaSearchProvider> providers, YtdlpService ytdlp, SettingsStore settings)
    {
        _settings = settings;
        _ytdlp = ytdlp;
        IMediaSearchProvider[] sources = providers.ToArray();
        Providers = sources.Length > 1 ? [new AllSearchProvider(sources), .. sources] : sources;
        SelectedProvider = Providers.FirstOrDefault(provider => provider.Id == settings.Current.ProviderId) ?? sources[0];
        Sort = settings.Current.Sort;
        // Recent picks saved before sources were recorded get theirs from the link.
        List<MediaSearchResult> recents = settings.Current.RecentResults;
        for (int i = 0; i < recents.Count; i++)
            recents[i] = recents[i] with { ProviderId = recents[i].ProviderId ?? GuessProvider(recents[i].SourceUrl) };
        Recents = new ObservableCollection<MediaSearchResult>(recents);
        Recents.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasRecents));
    }

    /// <summary>Every source together, then each on its own.</summary>
    public IReadOnlyList<IMediaSearchProvider> Providers { get; }
    public ObservableCollection<SearchSuggestion> Suggestions { get; } = [];

    /// <summary>Everything picked recently, from every source, newest first. Kept in step with the saved list.</summary>
    public ObservableCollection<MediaSearchResult> Recents { get; }

    public bool HasRecents => Recents.Count > 0;

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Placeholder))]
    public partial IMediaSearchProvider SelectedProvider { get; set; }

    /// <summary>Which results come first; asked of the sources that can order their own, and applied to what comes back.</summary>
    [ObservableProperty]
    public partial SearchSort Sort { get; set; }

    [ObservableProperty]
    public partial bool IsSearching { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlayerObscured))]
    public partial bool IsSuggestionListOpen { get; set; }

    /// <summary>The source list or the sort menu.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlayerObscured))]
    public partial bool IsMenuOpen { get; set; }

    /// <summary>Web players swallow input meant for popups drawn over them, so they step aside while one's open.</summary>
    public bool IsPlayerObscured => IsSuggestionListOpen || IsMenuOpen;

    public string Placeholder => $"Search {(IsEverySource ? "every source" : SelectedProvider.DisplayName)} or paste a link";

    private bool IsEverySource => SelectedProvider is AllSearchProvider;

    /// <summary>Details already read while opening a pasted link, so they aren't read twice.</summary>
    public MediaDetails? TakeLinkDetails(MediaSearchResult media) =>
        _linkDetails.Remove(media.SourceUrl, out MediaDetails? details) ? details : null;

    /// <summary>On focus: recent picks for an empty box, otherwise the results for what's typed.</summary>
    public void ShowSuggestions()
    {
        if (SearchText.Trim().Length == 0)
        {
            ShowRecents();
            return;
        }

        if (_shownQuery != SearchText.Trim())
            _ = SearchAsync(debounce: false);
        else if (Suggestions.Count > 0)
            IsSuggestionListOpen = true;
    }

    /// <summary>Searches once typing pauses; each keystroke restarts the wait.</summary>
    public Task SearchAsync(bool debounce = true)
    {
        if (SearchText.Trim().Length == 0)
        {
            Cancel();
            IsSearching = false;
            ShowRecents();
            return Task.CompletedTask;
        }

        return RunSearchAsync(SearchText.Trim(), debounce);
    }

    public void Remember(MediaSearchResult media)
    {
        Cancel();
        IsSearching = false;
        IsSuggestionListOpen = false;

        // Moved to the top in place, so lists showing the recents change once rather than being rebuilt.
        int existing = Recents.ToList().FindIndex(recent => recent.SourceUrl == media.SourceUrl);
        if (existing >= 0)
            Recents.RemoveAt(existing);
        Recents.Insert(0, media);
        while (Recents.Count > RecentLimit)
            Recents.RemoveAt(Recents.Count - 1);

        _settings.Current.RecentResults = [.. Recents];
        _settings.Save();
    }

    public void ClearRecents()
    {
        _settings.Current.RecentResults.Clear();
        _settings.Save();
        Recents.Clear();
        foreach (SearchSuggestion recent in Suggestions.Where(suggestion => suggestion.IsRecent).ToArray())
            Suggestions.Remove(recent);
    }

    partial void OnSelectedProviderChanged(IMediaSearchProvider value)
    {
        _settings.Current.ProviderId = value.Id;
        _settings.Save();
        Cancel();
        IsSearching = false;
        _shownQuery = null;
        Suggestions.Clear();

        string query = SearchText.Trim();
        if (query.Length > 0 && !IsLink(query) && _results.TryGetValue(value.Id, out var cached) && cached.Query == query && cached.Sort == Sort)
        {
            Show(MatchingRecents(query, RecentMatchLimit), cached.Results, query);
            _shownQuery = query;
        }
    }

    partial void OnSortChanged(SearchSort value)
    {
        if (_settings.Current.Sort == value)
            return;

        _settings.Current.Sort = value;
        _settings.Save();
        if (SearchText.Trim().Length > 0)
            _ = SearchAsync(debounce: false);
    }

    private async Task RunSearchAsync(string query, bool debounce)
    {
        IMediaSearchProvider provider = SelectedProvider;
        SearchSort sort = Sort;
        CancellationTokenSource search = Restart();
        CancellationToken token = search.Token;
        IsSearching = true;

        // Recent picks that match show straight away; results fill in below them.
        Show(MatchingRecents(query, RecentMatchLimit), [], query);

        try
        {
            if (debounce)
                await Task.Delay(DebounceDelay, token);

            IReadOnlyList<MediaSearchResult> results = [];
            if (IsLink(query))
            {
                results = [await OpenLinkAsync(query, token)];
            }
            else if (provider is AllSearchProvider everySource)
            {
                // Shown as each source answers, the list reordering as more arrive.
                await foreach (IReadOnlyList<MediaSearchResult> soFar in everySource.SearchEachAsync(query, sort, token))
                {
                    results = soFar;
                    Show(MatchingRecents(query, RecentMatchLimit), results, query);
                    IsSuggestionListOpen = true;
                }
            }
            else
            {
                results = SearchRanking.Order([await provider.SearchAsync(query, sort, token)], query, sort);
            }

            token.ThrowIfCancellationRequested();
            if (!IsLink(query))
                _results[provider.Id] = (query, sort, results);
            Show(MatchingRecents(query, RecentMatchLimit), results, query);
            if (Suggestions.Count == 0)
                Suggestions.Add(new SearchSuggestion(null, Message: $"No results for \"{query}\"", Query: query));
            _shownQuery = query;
            IsSuggestionListOpen = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (!token.IsCancellationRequested)
        {
            // Said in the list, where the results would have been; choosing it searches again.
            string what = IsLink(query) ? "open that link" : provider is AllSearchProvider ? "search" : $"search {provider.DisplayName}";
            Suggestions.Clear();
            Suggestions.Add(new SearchSuggestion(null, Message: $"Couldn't {what}. {exception.Message}", Query: query));
            _shownQuery = null;
            IsSuggestionListOpen = true;
        }
        finally
        {
            if (ReferenceEquals(_search, search))
                IsSearching = false;
        }
    }

    /// <summary>Reads a pasted link into media, keeping its details for when it's opened.</summary>
    public async Task<MediaSearchResult> OpenLinkAsync(string url, CancellationToken token = default)
    {
        (MediaSearchResult media, MediaDetails details) = await _ytdlp.OpenLinkAsync(url, token);
        _linkDetails[media.SourceUrl] = details;
        return media;
    }

    private void ShowRecents()
    {
        Show(MatchingRecents(string.Empty, RecentLimit), [], string.Empty);
        _shownQuery = string.Empty;
        IsSuggestionListOpen = Suggestions.Count > 0;
    }

    private IEnumerable<MediaSearchResult> MatchingRecents(string query, int limit)
    {
        if (IsLink(query))
            return [];

        string[] words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return _settings.Current.RecentResults
            .Where(recent => IsEverySource || recent.ProviderId == SelectedProvider.Id)
            .Where(recent => words.All(word => $"{recent.Title} {recent.Creator}".Contains(word, StringComparison.CurrentCultureIgnoreCase)))
            .Take(limit);
    }

    private void Show(IEnumerable<MediaSearchResult> recents, IReadOnlyList<MediaSearchResult> results, string query)
    {
        MediaSearchResult[] shownRecents = recents.ToArray();
        Suggestions.Clear();
        foreach (MediaSearchResult recent in shownRecents)
            Suggestions.Add(new SearchSuggestion(recent, IsRecent: true, Query: query, ShowSource: IsEverySource));
        foreach (MediaSearchResult result in results.Where(result => shownRecents.All(recent => recent.SourceUrl != result.SourceUrl)))
            Suggestions.Add(new SearchSuggestion(result, Query: query, ShowSource: IsEverySource));
    }

    internal static bool IsLink(string text) =>
        Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https";

    // Recent picks saved before sources were recorded; the site says which source they came from.
    private static string? GuessProvider(string url) =>
        url.Contains("soundcloud.com", StringComparison.OrdinalIgnoreCase) ? "soundcloud"
        : url.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) || url.Contains("youtu.be", StringComparison.OrdinalIgnoreCase) ? "youtube"
        : null;

    private CancellationTokenSource Restart()
    {
        Cancel();
        _search = new CancellationTokenSource();
        return _search;
    }

    private void Cancel()
    {
        _search?.Cancel();
        _search?.Dispose();
        _search = null;
    }
}
