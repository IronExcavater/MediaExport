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

/// <summary>A search that was carried out earlier, in the parts it is listed in for doing again.</summary>
/// <param name="FullText">The whole search as text, as <see cref="SearchViewModel.FullText"/> writes it.</param>
/// <param name="ProviderIcons">The resource keys of its sources' icons.</param>
/// <param name="Terms">Its terms other than the source and the ranking.</param>
/// <param name="Words">The words it searches for.</param>
public sealed record RecentSearch(string FullText, IReadOnlyList<string> ProviderIcons, IReadOnlyList<SearchTerm> Terms, string Words);

/// <summary>Coordinates the shared search query, selected source, and current results.</summary>
public sealed partial class SearchViewModel : ObservableObject
{
    private const int PageSize = 20;
    private const int RecentSearchCapacity = 10;

    private readonly SearchCache _cache;

    // Cancels the search in progress when a newer one starts.
    private CancellationTokenSource? _running;

    public SearchViewModel(IEnumerable<IMediaSearchProvider> providers, SearchCache cache)
    {
        _cache = cache;
        Providers = providers.ToList();
        if (Providers.Count > 0)
            SelectedProviders.Add(Providers[0]);

        SelectedProviders.CollectionChanged += (_, _) =>
        {
            StartFromFirstPage();
            OnPropertyChanged(nameof(FullText));
        };
        Terms.CollectionChanged += (_, _) =>
        {
            StartFromFirstPage();
            OnPropertyChanged(nameof(FullText));
        };
    }

    /// <summary>All search providers, in the order they were registered.</summary>
    public IReadOnlyList<IMediaSearchProvider> Providers { get; }

    /// <summary>The providers searched, in <see cref="Providers"/> order. Empty only if there are none.</summary>
    public ObservableCollection<IMediaSearchProvider> SelectedProviders { get; } = [];

    /// <summary>How the providers are asked to rank what they find.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FullText))]
    public partial SearchSort Sort { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FullText))]
    public partial string Query { get; set; } = "";

    /// <summary>The terms that narrow the search, other than its source.</summary>
    public ObservableCollection<SearchTerm> Terms { get; } = [];

    /// <summary>The searches that were committed to with <see cref="CommitSearchAsync"/>, newest first.</summary>
    public ObservableCollection<RecentSearch> RecentSearches { get; } = [];

    /// <summary>The query the results are for. It lags behind <see cref="Query"/> while that is being typed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial string SearchedQuery { get; private set; } = "";

    /// <summary>Which page of the results is showing, counting from 1.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    public partial int Page { get; private set; } = 1;

    /// <summary>Whether there may be another page. A full page means so, until the next comes back empty.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    public partial bool HasNextPage { get; private set; }

    private bool HasPreviousPage => Page > 1;

    // The last page of the current search, once the page after it has been asked for and had nothing on it.
    private int? _lastPage;

    // The page that last had results to show, which is gone back to from a page that has none.
    private int _shownPage = 1;

    // A different search starts again from its first page, with its last page unknown.
    partial void OnQueryChanged(string value) => StartFromFirstPage();

    partial void OnSortChanged(SearchSort value) => StartFromFirstPage();

    /// <summary>Chooses exactly these providers, in <see cref="Providers"/> order. Choosing none is ignored.</summary>
    public void SelectProviders(IEnumerable<IMediaSearchProvider> providers)
    {
        var chosen = Providers.Where(providers.Contains).ToList();
        if (chosen.Count == 0 || chosen.SequenceEqual(SelectedProviders))
            return;

        SelectedProviders.Clear();
        foreach (var provider in chosen)
            SelectedProviders.Add(provider);
    }

    // The sources as they are named in a message: the provider's name, or how many there are.
    private string SourceName => SelectedProviders.Count == 1 ? SelectedProviders[0].DisplayName : $"{SelectedProviders.Count} sources";

    private void StartFromFirstPage()
    {
        Page = 1;
        _lastPage = null;
        _shownPage = 1;
    }

    /// <summary>The whole search as text, such as <c>source:youtube sort:views exact:"lofi beats" study</c>.</summary>
    public string FullText
    {
        get
        {
            var terms = new List<SearchTerm>();
            if (SelectedProviders.Count > 0)
                terms.Add(SearchQuery.Source(SelectedProviders.Select(provider => provider.Id)));
            if (Sort != SearchSort.Relevance)
                terms.Add(new SearchTerm(SearchQuery.SortLabel, Sort.ToString().ToLowerInvariant()));
            terms.AddRange(Terms);

            return new SearchQuery(Query.Trim(), terms).ToString();
        }
    }

    /// <summary>Replaces the search with one written as text, such as a pasted one.</summary>
    public void Replace(string text)
    {
        Terms.Clear();
        var hasTerms = ResolveTerms(text);
        if (!hasTerms)
            Query = text.Trim();
    }

    /// <summary>Applies the terms written in the text and leaves the words in <see cref="Query"/>.</summary>
    /// <returns>Whether there were any terms.</returns>
    public bool ResolveTerms(string text)
    {
        var search = SearchQuery.Parse(text, IsKnownTerm);
        if (search.Terms.Count == 0)
            return false;

        foreach (var term in search.Terms)
            Apply(term);

        Query = search.Text;
        return true;
    }

    // A new kind of term is added in these two methods: one says whether it is valid, the other applies it.
    private bool IsKnownTerm(SearchTerm term) => term.Label switch
    {
        SearchQuery.SourceLabel => term.Values.Count > 0 && term.Values.All(id => FindProvider(id) is not null),
        SearchQuery.SortLabel => Enum.TryParse<SearchSort>(term.Value, ignoreCase: true, out _),
        SearchQuery.ExactLabel or SearchQuery.ExcludeLabel or SearchQuery.AuthorLabel => true,
        SearchQuery.DurationLabel or SearchQuery.PublishedLabel => SearchQuery.IsValid(term),
        // One end of a range written by itself is known if the range it makes is.
        _ => SearchQuery.RangeLabelOf(term.Label) is not null && SearchQuery.IsValid(SearchQuery.WithEnd(null, term))
    };

    private void Apply(SearchTerm term)
    {
        if (term.Label == SearchQuery.SourceLabel)
            SelectProviders(term.Values.Select(FindProvider).OfType<IMediaSearchProvider>());
        else if (term.Label == SearchQuery.SortLabel)
            Sort = Enum.Parse<SearchSort>(term.Value, ignoreCase: true);
        else if (SearchQuery.RangeLabelOf(term.Label) is not null)
            ApplyRangeEnd(term);
        else if (!Terms.Contains(term))
            AddTerm(term);
    }

    // One end of a range written by itself, such as longer:3m, becomes part of the search's range term.
    private void ApplyRangeEnd(SearchTerm end)
    {
        var rangeLabel = SearchQuery.RangeLabelOf(end.Label);
        var range = Terms.FirstOrDefault(term => term.Label == rangeLabel);
        var changed = SearchQuery.WithEnd(range, end);
        if (range is null)
            AddTerm(changed);
        else
            ReplaceTerm(range, changed);
    }

    /// <summary>Adds a term. Past <see cref="SearchQuery.MaxCount"/> of its kind, the oldest makes way.</summary>
    public void AddTerm(SearchTerm term)
    {
        var sameKind = Terms.Where(other => other.Label == term.Label).ToList();
        var excess = sameKind.Count + 1 - SearchQuery.MaxCount(term.Label);
        foreach (var oldest in sameKind.Take(excess))
            Terms.Remove(oldest);

        Terms.Add(term);
    }

    /// <summary>Puts an edited term in the place of the one it was edited from.</summary>
    public void ReplaceTerm(SearchTerm term, SearchTerm edited)
    {
        var index = Terms.IndexOf(term);
        if (index < 0)
            return;

        Terms[index] = edited;
    }

    private IMediaSearchProvider? FindProvider(string id) =>
        Providers.FirstOrDefault(provider => string.Equals(provider.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The page of results that is showing.</summary>
    public ObservableCollection<MediaSearchResult> Results { get; } = [];

    /// <summary>The first page of the current search, whichever page is showing, for the search field's list.</summary>
    public ObservableCollection<MediaSearchResult> FirstPageResults { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial bool IsSearching { get; private set; }

    /// <summary>Explains an empty result list (nothing found, or an error); otherwise <c>null</c>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial string? Message { get; private set; }

    /// <summary>What the results are: how they are ranked, what was searched for and what narrows them.</summary>
    public string Title
    {
        get
        {
            var search = _shownSearch;
            var exact = search.Terms.Where(term => term.Label == SearchQuery.ExactLabel).Select(term => $"\"{term.Value}\"");
            var asked = string.Join(' ', exact.Prepend(search.Text).Where(part => part.Length > 0));
            var hasWords = asked.Length > 0;

            var ranking = hasWords ? "Results" : "Popular";
            var title = _shownSort == SearchSort.Views ? "Most viewed" : ranking;
            if (hasWords)
                title += $" for {asked}";

            var author = search.Terms.FirstOrDefault(term => term.Label == SearchQuery.AuthorLabel)?.Value;
            if (!string.IsNullOrEmpty(author))
                title += $" by {author}";

            // The other terms follow in the words of their tooltips.
            var others = search.Terms.Where(term => term.Label is SearchQuery.ExcludeLabel or SearchQuery.DurationLabel or SearchQuery.PublishedLabel);
            foreach (var explanation in others.Select(SearchTermText.Explain).Where(explanation => explanation.Length > 0))
                title += $", {char.ToLowerInvariant(explanation[0])}{explanation[1..]}";

            return title;
        }
    }

    // The search and ranking the results are for, which Title describes.
    private SearchQuery _shownSearch = new(string.Empty, []);
    private SearchSort _shownSort;

    /// <summary>Which results are showing, or why there are none. Empty while a search is still going.</summary>
    public string Summary
    {
        get
        {
            if (Message is not null)
                return Message;

            if (IsSearching || Results.Count == 0)
                return string.Empty;

            var first = (Page - 1) * PageSize + 1;
            var last = first + Results.Count - 1;
            return $"Results {first} to {last}";
        }
    }

    [RelayCommand(CanExecute = nameof(HasPreviousPage))]
    private Task PreviousPageAsync() => ShowPageAsync(Page - 1);

    [RelayCommand(CanExecute = nameof(HasNextPage))]
    private Task NextPageAsync() => ShowPageAsync(Page + 1);

    /// <summary>Shows a page by its number. One past the end of the search goes back to the page that was showing.</summary>
    public Task GoToPageAsync(int page) => ShowPageAsync(Math.Max(1, page));

    // Pages are stepped one at a time, as a source reads every result before a page and doesn't say how many exist.
    private Task ShowPageAsync(int page)
    {
        Page = page;
        return SearchAsync();
    }

    /// <summary>Fills <see cref="RecentSearches"/> with searches written as text, such as ones saved earlier.</summary>
    public void LoadRecentSearches(IEnumerable<string> searches)
    {
        RecentSearches.Clear();
        foreach (var text in searches)
        {
            var recent = ToRecentSearch(text);
            var isListed = RecentSearches.Any(earlier => IsSame(earlier, recent));
            if (!isListed)
                RecentSearches.Add(recent);
        }
    }

    /// <summary>Searches and records the search as a recent one. Not for a search made while typing.</summary>
    [RelayCommand]
    public Task CommitSearchAsync()
    {
        var hasSearch = Query.Trim().Length > 0 || Terms.Count > 0;
        if (hasSearch)
            Remember(FullText);

        return SearchAsync();
    }

    // A search done again moves to the front instead of being listed twice.
    private void Remember(string fullText)
    {
        var recent = ToRecentSearch(fullText);
        var earlier = RecentSearches.Where(listed => IsSame(listed, recent)).ToList();
        foreach (var duplicate in earlier)
            RecentSearches.Remove(duplicate);

        RecentSearches.Insert(0, recent);
        while (RecentSearches.Count > RecentSearchCapacity)
            RecentSearches.RemoveAt(RecentSearches.Count - 1);
    }

    // Two searches are the same when they are listed the same, however each was written.
    private static bool IsSame(RecentSearch one, RecentSearch other) =>
        one.ProviderIcons.SequenceEqual(other.ProviderIcons)
        && one.Terms.SequenceEqual(other.Terms)
        && string.Equals(one.Words, other.Words, StringComparison.OrdinalIgnoreCase);

    private RecentSearch ToRecentSearch(string fullText)
    {
        var search = SearchQuery.Parse(fullText, IsKnownTerm);
        var source = search.Terms.LastOrDefault(term => term.Label == SearchQuery.SourceLabel);
        var sourceIds = source?.Values ?? [];
        var icons = sourceIds.Select(FindProvider).OfType<IMediaSearchProvider>().Select(provider => provider.Icon).ToList();
        var terms = search.Terms.Where(term => term.Label is not (SearchQuery.SourceLabel or SearchQuery.SortLabel)).ToList();
        return new RecentSearch(fullText, icons, terms, search.Text);
    }

    /// <summary>Searches the selected providers and replaces <see cref="Results"/> with the newest response.</summary>
    [RelayCommand]
    public async Task SearchAsync()
    {
        _running?.Cancel();
        _running = null;
        IsSearching = false;

        // Ranges are applied here, to what the provider found, so changing one re-filters the remembered results.
        var search = new SearchQuery(Query.Trim(), Terms.ToList());
        var query = search.ToSearchText();
        var page = Page;
        Results.Clear();
        if (page == 1)
            FirstPageResults.Clear();
        Message = null;
        HasNextPage = false;
        SearchedQuery = query;
        _shownSearch = search;
        _shownSort = Sort;
        OnPropertyChanged(nameof(Title));

        // A link is opened, not searched for. An empty search is made all the same, for what is popular.
        var isLink = SearchQuery.ReadLink(Query) is not null;
        if (isLink)
            return;

        var providers = SelectedProviders.ToList();
        if (providers.Count == 0)
        {
            Message = "There are no sources to search yet";
            return;
        }

        // Remembered by everything that decides the results: the providers, their ranking, the search and the page.
        var sources = string.Join(',', providers.Select(provider => provider.Id));
        var rankedQuery = Sort == SearchSort.Relevance ? query : $"{query} {SearchQuery.SortLabel}:{Sort}";
        var remembered = _cache.Find(sources, rankedQuery, page);
        if (remembered is not null)
        {
            Show(remembered, search, page);
            return;
        }

        var isPastTheEnd = false;

        // Each search owns its token source and disposes it when the search ends, however it ends.
        using var running = new CancellationTokenSource();
        _running = running;
        IsSearching = true;
        try
        {
            var found = await FindAsync(providers, query, page, running.Token);
            // A newer search started while this one was waiting, so these results are out of date.
            if (running.IsCancellationRequested)
                return;

            // An empty page straight after one with results is how the end of a search is found. A page jumped to
            // may be far past the end, which says nothing about where the end is.
            isPastTheEnd = found.Count == 0 && page > 1;
            var isNextPage = page == _shownPage + 1;
            if (isPastTheEnd && isNextPage)
                _lastPage = _shownPage;
            else if (!isPastTheEnd)
                Show(found, search, page);

            // An empty answer may only be a passing fault at the source, so it isn't remembered.
            if (found.Count > 0)
                _cache.Store(sources, rankedQuery, page, found);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (!running.IsCancellationRequested)
        {
            Message = $"Search hit a snag. {exception.Message}";
        }
        finally
        {
            // Only the current search clears these, or a replaced one would turn off the newer one's busy indicator.
            if (_running == running)
            {
                _running = null;
                IsSearching = false;
            }
        }

        if (isPastTheEnd)
            await ShowPageAsync(_shownPage);
    }

    // A page is shared out equally among the providers, each asked for its share of it.
    private int ShareOfPage => (int)Math.Ceiling(PageSize / (double)Math.Max(1, SelectedProviders.Count));

    // Asks every provider for its share of the page at once, then deals their results out in turn to mix them.
    private async Task<IReadOnlyList<MediaSearchResult>> FindAsync(IReadOnlyList<IMediaSearchProvider> providers, string query, int page, CancellationToken cancellationToken)
    {
        var share = ShareOfPage;
        var skip = (page - 1) * share;
        var searches = providers.Select(provider => provider.SearchAsync(query, skip, share, Sort, cancellationToken));
        var shares = await Task.WhenAll(searches);

        var mixed = new List<MediaSearchResult>();
        for (var index = 0; index < share; index++)
        {
            foreach (var found in shares)
            {
                if (index < found.Count)
                    mixed.Add(found[index]);
            }
        }
        return mixed;
    }

    private void Show(IReadOnlyList<MediaSearchResult> found, SearchQuery search, int page)
    {
        _shownPage = page;
        var withinRanges = found.Where(search.Matches).ToList();
        foreach (var result in withinRanges)
        {
            Results.Add(result);
            if (page == 1)
                FirstPageResults.Add(result);
        }

        // A source with nothing popular to list is not a search that failed.
        var isSearch = search.ToSearchText().Length > 0;
        if (found.Count == 0)
            Message = isSearch ? $"No results found in {SourceName}" : null;
        else if (withinRanges.Count == 0)
            Message = "Nothing on this page is within the search's ranges";
        else
            Message = null;

        // Another page depends on what the providers found, not how much is shown: one filling its share is enough.
        var isLastPage = page == _lastPage;
        HasNextPage = found.Count >= ShareOfPage && !isLastPage;
        OnPropertyChanged(nameof(Summary));
    }
}
