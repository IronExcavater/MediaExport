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

/// <summary>A line in the search box's list: a result, a search made before, or a message where results would be.</summary>
/// <param name="Query">What the box shows while the line is highlighted: the search it belongs to, or for an earlier search, that search.</param>
public sealed record SearchSuggestion(MediaSearchResult? Media, string? Message = null, string Query = "", bool IsRecentSearch = false)
{
    public bool IsMessage => Media is null && !IsRecentSearch;

    // Suggestions are announced by screen readers using ToString.
    public override string ToString() => IsRecentSearch ? $"Searched before: {Query}" : Message ?? $"{Media}";
}

/// <summary>
/// The title bar search. A search is the words in the box, the labelled terms beside it as pills, and
/// the source it's aimed at; here it's asked of one source as you type. Every source together is the
/// Search page's (<see cref="ResultsViewModel"/>), which takes its words, terms and order from here.
/// </summary>
public sealed partial class SearchViewModel : ObservableObject
{
    public const int RecentLimit = 24;
    private const int RecentSearchLimit = 12;
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(400);

    private readonly SettingsStore _settings;
    private readonly Func<string, CancellationToken, Task<OpenedLink>> _openLink;
    private readonly Dictionary<string, MediaDetails> _linkDetails = [];

    // Each source's last results, so switching sources back and forth doesn't search again.
    private readonly Dictionary<string, (string Query, SearchSort Sort, SearchArrangement Arrangement, IReadOnlyList<MediaSearchResult> Results)> _results = [];
    private CancellationTokenSource? _search;
    private string? _shownQuery;
    private bool _changingDraft;

    public SearchViewModel(IEnumerable<IMediaSearchProvider> providers, YtdlpService ytdlp, SettingsStore settings)
        : this(providers, settings, ytdlp.OpenLinkResultsAsync)
    {
    }

    internal SearchViewModel(
        IEnumerable<IMediaSearchProvider> providers,
        SettingsStore settings,
        Func<string, CancellationToken, Task<OpenedLink>> openLink)
    {
        _settings = settings;
        _openLink = openLink;
        Providers = providers.ToArray();
        SelectedProvider = Providers.FirstOrDefault(provider => provider.Id == settings.Current.ProviderId) ?? Providers[0];
        // A saved order this version no longer has falls back to the usual one.
        Sort = Enum.IsDefined(settings.Current.Sort) ? settings.Current.Sort : SearchSort.Relevance;
        Arrangement = Enum.IsDefined(settings.Current.Arrangement) ? settings.Current.Arrangement : SearchArrangement.AsRanked;

        Recents = new ObservableCollection<MediaSearchResult>(settings.Current.RecentResults);
        Recents.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasRecents));
        RecentSearches = new ObservableCollection<string>(settings.Current.RecentSearches);
        RecentSearches.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasRecentSearches));
        Draft.Changed += (_, _) => OnDraftChanged();
    }

    public IReadOnlyList<IMediaSearchProvider> Providers { get; }
    public ObservableCollection<SearchSuggestion> Suggestions { get; } = [];

    /// <summary>The editable words and structured terms rendered by the unified search input.</summary>
    public SearchDraft Draft { get; } = new();

    /// <summary>Media opened recently, from every source, newest first. Kept in step with the saved list.</summary>
    public ObservableCollection<MediaSearchResult> Recents { get; }

    /// <summary>Searches made recently, newest first, as they'd be typed.</summary>
    public ObservableCollection<string> RecentSearches { get; }

    public bool HasRecents => Recents.Count > 0;
    public bool HasRecentSearches => RecentSearches.Count > 0;

    /// <summary>The ordinary words in the editable search.</summary>
    public string SearchText
    {
        get => Draft.Text;
        set
        {
            if (Draft.Text == value)
                return;
            Draft.Text = value;
            OnPropertyChanged();
            QueryTextChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [ObservableProperty]
    public partial IMediaSearchProvider SelectedProvider { get; set; }

    /// <summary>
    /// What decides which results come back, here and on the Search page: the best matches or the most viewed.
    /// Asked of the sources that can order their own, and applied to what comes back.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortLabel))]
    public partial SearchSort Sort { get; set; }

    /// <summary>How those results are then laid out: as ranked, by date, or by title.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortLabel))]
    public partial SearchArrangement Arrangement { get; set; }

    [ObservableProperty]
    public partial bool IsSearching { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlayerObscured))]
    public partial bool IsSuggestionListOpen { get; set; }

    /// <summary>A menu or a term's editor is open from the search bar.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlayerObscured))]
    public partial bool IsMenuOpen { get; set; }

    /// <summary>
    /// True on the Search page, which takes the box over: what's typed is asked of every source and shown
    /// on the page, so there's no source to choose here and no list of suggestions.
    /// </summary>
    [ObservableProperty]
    public partial bool IsPageSearch { get; set; }

    /// <summary>Web players swallow input meant for popups drawn over them, so they step aside while one's open.</summary>
    public bool IsPlayerObscured => IsSuggestionListOpen || IsMenuOpen;

    public string Placeholder => "Search or paste a link";

    /// <summary>Both parts of the order in a few words, e.g. "Relevancy" or "Popularity, newest first".</summary>
    public string SortLabel => (Sort == SearchSort.Popularity ? "Popularity" : "Relevancy") + Arrangement switch
    {
        SearchArrangement.NewestFirst => ", newest first",
        SearchArrangement.OldestFirst => ", oldest first",
        SearchArrangement.TitleAToZ => ", A to Z",
        SearchArrangement.TitleZToA => ", Z to A",
        _ => string.Empty
    };

    /// <summary>The search as it's run: the box's words and the pills' terms, as one line of text.</summary>
    public string QueryText => Draft.QueryText;

    /// <summary>
    /// The whole search as text, for copying: <see cref="QueryText"/> plus the source it's aimed at.
    /// Pasted back in, it's the same search again.
    /// </summary>
    public string FullText => Draft.Serialize((IsPageSearch ? PageSources() : [SelectedProvider]).Select(source => source.Id));

    /// <summary>The sources the Search page has narrowed its search to (none when it's searching them all); that page keeps this up to date.</summary>
    public Func<IEnumerable<IMediaSearchProvider>> PageSources { get; set; } = () => [];

    /// <summary>On the Search page, <c>source:</c> terms were typed; that page picks the sources they name.</summary>
    public event EventHandler<IReadOnlyList<IMediaSearchProvider>>? SourcesNamed;

    /// <summary>A pill was added, removed or given another value, so the search is no longer what was last run.</summary>
    public event EventHandler? TermsChanged;

    /// <summary>The ordinary text changed, so the Search page can debounce words or clear itself for a link.</summary>
    public event EventHandler? QueryTextChanged;

    /// <summary>Details already read while opening a pasted link, so they aren't read twice.</summary>
    public MediaDetails? TakeLinkDetails(MediaSearchResult media) =>
        _linkDetails.Remove(media.SourceUrl, out MediaDetails? details) ? details : null;

    /// <summary>On focus: earlier searches for an empty box, otherwise the results for what's typed.</summary>
    public void ShowSuggestions()
    {
        if (QueryText.Length == 0)
            ShowRecentSearches();
        else if (_shownQuery != QueryText)
            _ = SearchAsync(debounce: false);
        else if (Suggestions.Count > 0)
            IsSuggestionListOpen = true;
    }

    /// <summary>Stops title-bar suggestions while the Search page owns an ordinary word search.</summary>
    public void ResetSuggestions()
    {
        Cancel();
        IsSearching = false;
        IsSuggestionListOpen = false;
        _shownQuery = null;
        Suggestions.Clear();
    }

    /// <summary>
    /// Lifts any labelled terms out of the box's text and acts on them: <c>source:</c> chooses the source
    /// (here, or on the Search page), and the rest become pills. A term has done its job as text once
    /// it's somewhere it can be seen and changed properly.
    /// </summary>
    /// <param name="includeUnfinished">Whether a term at the very end counts; it doesn't while it may still be being typed.</param>
    /// <returns>Whether the box's text changed.</returns>
    public bool ResolveTerms(bool includeUnfinished)
    {
        string before = Draft.QueryText;
        _changingDraft = true;
        IReadOnlyList<string> named;
        try
        {
            named = Draft.Resolve(includeUnfinished, name => FindSource(name) is not null);
        }
        finally
        {
            _changingDraft = false;
        }

        IMediaSearchProvider[] sources = RouteSources(named);

        bool changed = before != Draft.QueryText || sources.Length > 0;
        if (changed)
            OnStructuredDraftChanged();
        return changed;
    }

    /// <summary>Puts a search made before back in the input without running it; its caller submits it once.</summary>
    public void Restore(string search) => ReplaceDraft(search, notify: false);

    /// <summary>Replaces every editable part of the field, routing copied source terms and searching once.</summary>
    public void Replace(string search) => ReplaceDraft(search, notify: true);

    private void ReplaceDraft(string search, bool notify)
    {
        _changingDraft = true;
        IReadOnlyList<string> named;
        try
        {
            Draft.Cut();
            Draft.Text = search;
            named = Draft.Resolve(includeUnfinished: true, name => FindSource(name) is not null);
        }
        finally
        {
            _changingDraft = false;
        }
        RouteSources(named);
        OnPropertyChanged(nameof(SearchText));
        OnPropertyChanged(nameof(QueryText));
        OnPropertyChanged(nameof(FullText));
        if (notify)
            OnStructuredDraftChanged();
    }

    private IMediaSearchProvider[] RouteSources(IEnumerable<string> named)
    {
        IMediaSearchProvider[] sources = named.Select(name => FindSource(name)!).Distinct().ToArray();
        if (sources.Length > 0 && IsPageSearch)
            SourcesNamed?.Invoke(this, sources);
        else if (sources.Length > 0)
            SelectedProvider = sources[^1];
        return sources;
    }

    /// <summary>Searches once typing pauses; each keystroke restarts the wait.</summary>
    public Task SearchAsync(bool debounce = true)
    {
        if (QueryText.Length == 0)
        {
            Cancel();
            IsSearching = false;
            ShowRecentSearches();
            return Task.CompletedTask;
        }

        return RunSearchAsync(QueryText, debounce);
    }

    /// <summary>Notes that media was opened: the list closes, and it goes to the top of the recently opened.</summary>
    public void Remember(MediaSearchResult media)
    {
        Cancel();
        IsSearching = false;
        IsSuggestionListOpen = false;
        MoveToTop(Recents, media, recent => recent.SourceUrl == media.SourceUrl, RecentLimit);
        _settings.Current.RecentResults = [.. Recents];
        _settings.Save();
    }

    /// <summary>Notes the search as it stands as one worth offering again.</summary>
    public void RememberSearch()
    {
        string search = QueryText;
        if (search.Length == 0 || IsLink(search))
            return;

        MoveToTop(RecentSearches, search, recent => string.Equals(recent, search, StringComparison.CurrentCultureIgnoreCase), RecentSearchLimit);
        _settings.Current.RecentSearches = [.. RecentSearches];
        _settings.Save();
    }

    public void ClearRecents()
    {
        Recents.Clear();
        _settings.Current.RecentResults.Clear();
        _settings.Save();
    }

    public void ClearRecentSearches()
    {
        RecentSearches.Clear();
        _settings.Current.RecentSearches.Clear();
        _settings.Save();
        foreach (SearchSuggestion recent in Suggestions.Where(suggestion => suggestion.IsRecentSearch).ToArray())
            Suggestions.Remove(recent);
    }

    partial void OnIsPageSearchChanged(bool value)
    {
        // Whatever the box was suggesting belongs to the page it was on.
        ResetSuggestions();
        OnPropertyChanged(nameof(FullText));
    }

    partial void OnSelectedProviderChanged(IMediaSearchProvider value)
    {
        _settings.Current.ProviderId = value.Id;
        _settings.Save();
        Cancel();
        IsSearching = false;
        _shownQuery = null;
        Suggestions.Clear();
        OnPropertyChanged(nameof(FullText));

        string query = QueryText;
        if (query.Length > 0 && !IsLink(query) && _results.TryGetValue(value.Id, out var cached) && cached.Query == query && cached.Sort == Sort && cached.Arrangement == Arrangement)
        {
            Show(cached.Results, query);
            _shownQuery = query;
        }
    }

    partial void OnSortChanged(SearchSort value) => OnOrderChanged();

    partial void OnArrangementChanged(SearchArrangement value) => OnOrderChanged();

    private void OnOrderChanged()
    {
        if (_settings.Current.Sort == Sort && _settings.Current.Arrangement == Arrangement)
            return;

        _settings.Current.Sort = Sort;
        _settings.Current.Arrangement = Arrangement;
        _settings.Save();
        if (!IsPageSearch && QueryText.Length > 0)
            _ = SearchAsync(debounce: false);
    }

    private void OnDraftChanged()
    {
        OnPropertyChanged(nameof(SearchText));
        OnPropertyChanged(nameof(QueryText));
        OnPropertyChanged(nameof(FullText));
        if (_changingDraft || Draft.LastChangeWasText)
            return;

        OnStructuredDraftChanged();
    }

    private void OnStructuredDraftChanged()
    {
        TermsChanged?.Invoke(this, EventArgs.Empty);
        if (!IsPageSearch || IsLink(QueryText.Trim()))
            _ = SearchAsync(debounce: false);
    }

    private async Task RunSearchAsync(string query, bool debounce)
    {
        IMediaSearchProvider provider = SelectedProvider;
        (SearchSort sort, SearchArrangement arrangement) = (Sort, Arrangement);
        CancellationTokenSource search = Restart();
        CancellationToken token = search.Token;
        IsSearching = true;
        Suggestions.Clear();

        try
        {
            if (debounce)
                await Task.Delay(DebounceDelay, token);

            IReadOnlyList<MediaSearchResult> results = IsLink(query)
                ? await OpenLinkResultsAsync(query, token)
                : await SearchSourceAsync(provider, SearchQuery.Parse(query), sort, arrangement, token);
            token.ThrowIfCancellationRequested();

            if (!IsLink(query))
                _results[provider.Id] = (query, sort, arrangement, results);
            Show(results, query);
            if (Suggestions.Count == 0)
                Suggestions.Add(new SearchSuggestion(null, $"No results for \"{query}\"", query));
            _shownQuery = query;
            IsSuggestionListOpen = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (!token.IsCancellationRequested)
        {
            // Said in the list, where the results would have been; choosing it searches again.
            string what = IsLink(query) ? "open that link" : $"search {provider.DisplayName}";
            Suggestions.Clear();
            Suggestions.Add(new SearchSuggestion(null, $"Couldn't {what}. {exception.Message}", query));
            _shownQuery = null;
            IsSuggestionListOpen = true;
        }
        finally
        {
            if (ReferenceEquals(_search, search))
                IsSearching = false;
        }
    }

    // The source is asked for the query's words; its other terms are then held against what comes back.
    private static async Task<IReadOnlyList<MediaSearchResult>> SearchSourceAsync(
        IMediaSearchProvider provider, SearchQuery query, SearchSort sort, SearchArrangement arrangement, CancellationToken token) =>
        query.Words.Length == 0 ? [] : query.Keep(SearchRanking.Order([await provider.SearchAsync(query.Words, sort, token)], query.Words, sort, arrangement));

    /// <summary>Reads a pasted link into media, keeping its details for when it's opened.</summary>
    public async Task<MediaSearchResult> OpenLinkAsync(string url, CancellationToken token = default)
    {
        IReadOnlyList<MediaSearchResult> media = await OpenLinkResultsAsync(url, token);
        return media.Count == 1 ? media[0] : throw new InvalidOperationException("That link contains a playlist. Choose one of its items.");
    }

    /// <summary>Reads a pasted link, returning every entry when it is a playlist.</summary>
    public async Task<IReadOnlyList<MediaSearchResult>> OpenLinkResultsAsync(string url, CancellationToken token = default)
    {
        OpenedLink opened = await _openLink(url, token);
        if (opened.Media.Count == 1 && opened.Details is { } details)
            _linkDetails[opened.Media[0].SourceUrl] = details;
        return opened.Media;
    }

    private void ShowRecentSearches()
    {
        Suggestions.Clear();
        foreach (string search in RecentSearches)
            Suggestions.Add(new SearchSuggestion(null, Query: search, IsRecentSearch: true));
        _shownQuery = string.Empty;
        IsSuggestionListOpen = Suggestions.Count > 0;
    }

    private void Show(IReadOnlyList<MediaSearchResult> results, string query)
    {
        Suggestions.Clear();
        foreach (MediaSearchResult result in results)
            Suggestions.Add(new SearchSuggestion(result, Query: SearchText));
    }

    // Moved in place, so lists showing these change once rather than being rebuilt.
    private static void MoveToTop<T>(ObservableCollection<T> items, T item, Func<T, bool> isSame, int limit)
    {
        if (items.ToList().FindIndex(existing => isSame(existing)) is >= 0 and var existing)
            items.RemoveAt(existing);
        items.Insert(0, item);
        while (items.Count > limit)
            items.RemoveAt(items.Count - 1);
    }

    // By its id or its name, however it's spaced or cased: youtubemusic, "YouTube Music".
    private IMediaSearchProvider? FindSource(string name) =>
        Providers.FirstOrDefault(provider => IsSame(provider.Id, name) || IsSame(provider.DisplayName, name));

    private static bool IsSame(string one, string other) =>
        string.Equals(one.Replace(" ", string.Empty), other.Replace(" ", string.Empty), StringComparison.OrdinalIgnoreCase);

    internal static bool IsLink(string text) =>
        Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https";

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
