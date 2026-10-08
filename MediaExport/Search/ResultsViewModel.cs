// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaExport.Core.Media.Models;
using MediaExport.Core.Media.Providers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MediaExport.Search;

public sealed partial class SourceFilter(IMediaSearchProvider provider) : ObservableObject
{
    public IMediaSearchProvider Provider => provider;

    [ObservableProperty]
    public partial bool IsPicked { get; set; } = true;
}

public sealed record SearchPageChoice(int? Number, bool IsCurrent)
{
    public string Label => Number?.ToString() ?? "…";
    public bool IsEllipsis => Number is null;
}

/// <summary>
/// Each numbered page asks the selected remote sources for its offsets. Completed pages are cached so going
/// back is immediate, while an unseen next page always performs new requests.
/// </summary>
public sealed partial class ResultsViewModel : ObservableObject
{
    private const int PageSize = 24;
    private static readonly TimeSpan TypingPause = TimeSpan.FromMilliseconds(500);

    private readonly SearchViewModel _search;
    private readonly Dictionary<CacheKey, PageSession> _sessions = [];
    private CancellationTokenSource? _running;
    private CacheKey? _currentKey;
    private PageSession? _currentSession;
    private bool _isSwitching;

    private sealed record CacheKey(string Token, string Sources, SearchSort Sort, SearchArrangement Arrangement);

    private sealed class PageSession(IReadOnlyList<IMediaSearchProvider> providers)
    {
        public IReadOnlyList<IMediaSearchProvider> Providers { get; } = providers;
        public Dictionary<int, SearchResultPage> Pages { get; } = [];
    }

    public ResultsViewModel(IEnumerable<IMediaSearchProvider> providers, SearchViewModel search)
    {
        _search = search;
        Sources = providers.Select(provider => new SourceFilter(provider)).ToArray();
        foreach (SourceFilter source in Sources)
        {
            source.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName != nameof(SourceFilter.IsPicked) || _isSwitching)
                    return;

                if (!source.IsPicked && Sources.All(other => other == source || other.IsPicked))
                    Pick(candidate => candidate == source);
                OnPicksChanged();
            };
        }

        search.PageSources = () => AreAllPicked ? [] : Sources.Where(source => source.IsPicked).Select(source => source.Provider);
        search.SourcesNamed += (_, named) =>
        {
            Pick(source => named.Contains(source.Provider));
            OnPropertyChanged(nameof(AreAllPicked));
        };
        search.TermsChanged += (_, _) =>
        {
            if (search.IsPageSearch)
                RefreshPageQuery(afterPause: false);
        };
        search.QueryTextChanged += (_, _) =>
        {
            if (search.IsPageSearch)
                RefreshPageQuery(afterPause: true);
        };
        search.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(SearchViewModel.Sort) or nameof(SearchViewModel.Arrangement))
                RefreshCurrentSearch();
        };
    }

    public IReadOnlyList<SourceFilter> Sources { get; }
    public ObservableCollection<MediaSearchResult> Results { get; } = [];
    public IReadOnlyList<int> LoadingSlots { get; } = Enumerable.Range(0, 8).ToArray();
    public bool AreAllPicked => Sources.All(source => source.IsPicked);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBlank))]
    public partial string Query { get; private set; } = string.Empty;

    public bool IsBlank => Query.Length == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShowingSkeletons))]
    public partial bool IsSearching { get; private set; }

    public bool IsShowingSkeletons => IsSearching && Results.Count == 0;

    [ObservableProperty]
    public partial string Summary { get; private set; } = "Search for media";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageLabel), nameof(CanGoBack), nameof(CanGoForward), nameof(HasPages))]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand), nameof(NextPageCommand))]
    public partial int Page { get; private set; } = 1;

    public int PageCount
    {
        get
        {
            int known = _currentSession?.Pages.Keys.DefaultIfEmpty(1).Max() ?? 1;
            return Math.Max(known, CanGoForward ? Page + 1 : Page);
        }
    }

    public string PageLabel => $"Page {Page}";
    public bool HasPages => Page > 1 || CanGoForward;
    public bool CanGoBack => Page > 1;
    public bool CanGoForward => CurrentPage?.HasMore == true;
    public IReadOnlyList<SearchPageChoice> PageChoices => BuildPageChoices();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEmptyState))]
    public partial string? EmptyTitle { get; private set; }

    public bool HasEmptyState => EmptyTitle is not null;
    private SearchResultPage? CurrentPage => _currentSession?.Pages.GetValueOrDefault(Page);

    public Task SearchAsync(string query, bool afterPause = false) =>
        StartAsync(query.Trim(), query.Trim(), $"search:{query.Trim()}", afterPause, isPopular: false);

    public Task LoadPopularAsync() =>
        StartAsync(string.Empty, string.Empty, "popular", afterPause: false, isPopular: true);

    private async Task StartAsync(string query, string shownQuery, string cacheToken, bool afterPause, bool isPopular)
    {
        if (_search.IsPageSearch && !SearchViewModel.IsLink(_search.QueryText.Trim()))
            _search.ResetSuggestions();
        _running?.Cancel();
        if (!isPopular && query.Length == 0)
        {
            Query = string.Empty;
            Page = 1;
            _currentKey = null;
            _currentSession = null;
            SetResults([]);
            EmptyTitle = null;
            IsSearching = false;
            return;
        }

        var running = _running = new CancellationTokenSource();
        try
        {
            if (afterPause)
                await Task.Delay(TypingPause, running.Token);

            bool isLink = SearchViewModel.IsLink(query);
            IMediaSearchProvider[] asked = Sources.Where(source => source.IsPicked).Select(source => source.Provider).ToArray();
            if (!isLink && asked.Length == 0)
            {
                Query = shownQuery;
                Page = 1;
                _currentKey = null;
                _currentSession = null;
                SetResults([]);
                EmptyTitle = "Pick a source to keep searching";
                return;
            }

            var key = new CacheKey(cacheToken, isLink ? "link" : string.Join('|', asked.Select(source => source.Id)), _search.Sort, _search.Arrangement);
            Query = shownQuery;
            Page = 1;
            _currentKey = key;
            _currentSession = _sessions.GetValueOrDefault(key) ?? (_sessions[key] = new PageSession(asked));
            if (_currentSession.Pages.TryGetValue(1, out SearchResultPage? cached))
            {
                Show(cached);
                EmptyTitle = cached.Results.Count == 0 ? EmptyFor(shownQuery, isPopular) : null;
                return;
            }

            SetResults([]);
            EmptyTitle = null;
            IsSearching = true;

            SearchResultPage found;
            if (isLink)
            {
                IReadOnlyList<MediaSearchResult> linked = await _search.OpenLinkResultsAsync(query, running.Token);
                found = new SearchResultPage(linked, false);
                if (!ReferenceEquals(_currentSession, _sessions[key]) || running.IsCancellationRequested)
                    return;
                _currentSession.Pages[1] = found;
                Show(found);
            }
            else
            {
                var everything = new AllSearchProvider(_currentSession.Providers, arrangement: _search.Arrangement);
                SearchQuery? terms = isPopular ? null : SearchQuery.Parse(query);
                if (terms is { Words.Length: 0 })
                {
                    found = new SearchResultPage([], false);
                    _currentSession.Pages[1] = found;
                    Show(found);
                }
                else
                {
                    IAsyncEnumerable<SearchResultPage> snapshots = isPopular
                        ? everything.PopularEachPageAsync(offset: 0, take: PageSize, running.Token)
                        : everything.SearchEachPageAsync(terms!.Words, _search.Sort, offset: 0, take: PageSize, running.Token);
                    found = new SearchResultPage([], false, false);
                    await foreach (SearchResultPage snapshot in snapshots)
                    {
                        if (!ReferenceEquals(_currentSession, _sessions[key]) || running.IsCancellationRequested)
                            return;
                        found = terms is null ? snapshot : Filter(snapshot, terms);
                        found = WithoutEarlierResults(_currentSession, found);
                        if (found.IsComplete)
                            _currentSession.Pages[1] = found;
                        Show(found);
                    }
                }
            }

            if (!ReferenceEquals(_currentSession, _sessions[key]) || running.IsCancellationRequested)
                return;
            EmptyTitle = found.Results.Count == 0
                ? isLink ? "That link had nothing to show" : EmptyFor(shownQuery, isPopular)
                : null;
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (!running.IsCancellationRequested)
        {
            EmptyTitle = isPopular ? $"Popular picks are taking a break. {exception.Message}" : $"Search hit a snag. {exception.Message}";
        }
        finally
        {
            if (ReferenceEquals(_running, running))
                IsSearching = false;
        }
    }

    [RelayCommand]
    private void PickAll()
    {
        if (AreAllPicked)
        {
            // A ToggleButton changes itself before executing its command. Reassert the source of truth so
            // clicking an already-active Everywhere control cannot leave its visual state unchecked.
            OnPropertyChanged(nameof(AreAllPicked));
            return;
        }
        Pick(_ => true);
        OnPicksChanged();
    }

    private void Pick(Func<SourceFilter, bool> isPicked)
    {
        _isSwitching = true;
        foreach (SourceFilter source in Sources)
            source.IsPicked = isPicked(source);
        _isSwitching = false;
    }

    private void OnPicksChanged()
    {
        OnPropertyChanged(nameof(AreAllPicked));
        RefreshCurrentSearch();
    }

    private void RefreshCurrentSearch()
    {
        if (SearchViewModel.IsLink(_search.QueryText.Trim()))
            return;
        RefreshPageQuery(afterPause: false);
    }

    private void RefreshPageQuery(bool afterPause)
    {
        string current = _search.QueryText.Trim();
        _ = SearchViewModel.IsLink(current) ? SearchAsync(string.Empty)
            : current.Length == 0 ? LoadPopularAsync()
            : SearchAsync(current, afterPause);
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private Task PreviousPageAsync() => GoToPageAsync(Page - 1);

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private Task NextPageAsync() => GoToPageAsync(Page + 1);

    [RelayCommand]
    private Task GoToPageAsync(int number)
    {
        if (number < 1 || _currentSession is null || _currentKey is null || number == Page)
            return Task.CompletedTask;
        if (number > Page && !CanGoForward && !_currentSession.Pages.ContainsKey(number))
            return Task.CompletedTask;
        return ReadPageAsync(number, _currentKey, _currentSession);
    }

    private async Task ReadPageAsync(int number, CacheKey key, PageSession session)
    {
        _running?.Cancel();
        var running = _running = new CancellationTokenSource();
        try
        {
            Page = number;
            if (session.Pages.TryGetValue(number, out SearchResultPage? cached))
            {
                Show(cached);
                EmptyTitle = cached.Results.Count == 0 ? EmptyFor(Query, key.Token == "popular") : null;
                return;
            }

            SetResults([]);
            EmptyTitle = null;
            IsSearching = true;
            var everything = new AllSearchProvider(session.Providers, arrangement: key.Arrangement);
            int offset = (number - 1) * PageSize;
            bool isPopular = key.Token == "popular";
            SearchQuery? terms = isPopular ? null : SearchQuery.Parse(Query);
            IAsyncEnumerable<SearchResultPage> snapshots = isPopular
                ? everything.PopularEachPageAsync(offset, PageSize, running.Token)
                : everything.SearchEachPageAsync(terms!.Words, key.Sort, offset, PageSize, running.Token);
            SearchResultPage found = new([], false, false);
            await foreach (SearchResultPage snapshot in snapshots)
            {
                if (!ReferenceEquals(_currentSession, session) || running.IsCancellationRequested)
                    return;
                found = terms is null ? snapshot : Filter(snapshot, terms);
                found = WithoutEarlierResults(session, found);
                if (found.IsComplete)
                    session.Pages[number] = found;
                Show(found);
            }

            if (!ReferenceEquals(_currentSession, session) || running.IsCancellationRequested)
                return;
            EmptyTitle = found.Results.Count == 0 ? EmptyFor(Query, isPopular) : null;
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (!running.IsCancellationRequested)
        {
            EmptyTitle = key.Token == "popular" ? $"Popular picks are taking a break. {exception.Message}" : $"Search hit a snag. {exception.Message}";
        }
        finally
        {
            if (ReferenceEquals(_running, running))
                IsSearching = false;
        }
    }

    private static SearchResultPage Filter(SearchResultPage page, SearchQuery terms) =>
        page with { Results = terms.Keep(page.Results) };

    private static SearchResultPage WithoutEarlierResults(PageSession session, SearchResultPage page)
    {
        var seen = session.Pages.Values
            .SelectMany(cached => cached.Results)
            .Select(result => result.SourceUrl)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return page with
        {
            Results = page.Results
                .Where(result => seen.Add(result.SourceUrl))
                .DistinctBy(result => result.SourceUrl, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private static string EmptyFor(string shownQuery, bool isPopular) => isPopular
        ? "Popular picks are still warming up"
        : $"No matches for “{shownQuery}”";

    private void Show(SearchResultPage page) => SetResults(page.Results);

    private void SetResults(IReadOnlyList<MediaSearchResult> found)
    {
        Results.Clear();
        foreach (MediaSearchResult result in found)
            Results.Add(result);

        int sources = found.Select(result => result.ProviderId).Distinct().Count();
        Summary = Query.Length == 0 ? "Search for media"
            : found.Count == 0 ? $"Results for “{Query}”"
            : $"{found.Count} {(found.Count == 1 ? "result" : "results")} from {sources} {(sources == 1 ? "source" : "sources")}";
        RaisePageState();
        OnPropertyChanged(nameof(IsShowingSkeletons));
    }

    private void RaisePageState()
    {
        OnPropertyChanged(nameof(PageCount));
        OnPropertyChanged(nameof(PageLabel));
        OnPropertyChanged(nameof(HasPages));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        OnPropertyChanged(nameof(PageChoices));
        PreviousPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
    }

    private IReadOnlyList<SearchPageChoice> BuildPageChoices()
    {
        if (!HasPages)
            return [];

        int last = PageCount;
        int[] shown = new[] { 1, Page - 1, Page, Page + 1, last }
            .Where(number => number >= 1 && number <= last)
            .Distinct()
            .Order()
            .ToArray();
        var choices = new List<SearchPageChoice>();
        for (int index = 0; index < shown.Length; index++)
        {
            if (index > 0 && shown[index] - shown[index - 1] > 1)
                choices.Add(new SearchPageChoice(null, false));
            choices.Add(new SearchPageChoice(shown[index], shown[index] == Page));
        }
        return choices;
    }
}
