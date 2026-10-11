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

/// <summary>A search that was carried out earlier, as it is listed for doing again.</summary>
/// <param name="FullText">The whole search as text, as <see cref="SearchViewModel.FullText"/> writes it.</param>
/// <param name="Words">The search without its source, which <paramref name="ProviderIcon"/> shows instead.</param>
/// <param name="ProviderIcon">The resource key of the source's icon, or <c>null</c> if the source is gone.</param>
public sealed record RecentSearch(string FullText, string Words, string? ProviderIcon);

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
        SelectedProvider = Providers.FirstOrDefault();
        Terms.CollectionChanged += (_, _) =>
        {
            StartFromFirstPage();
            OnPropertyChanged(nameof(FullText));
        };
    }

    /// <summary>All search providers, in the order they were registered.</summary>
    public IReadOnlyList<IMediaSearchProvider> Providers { get; }

    /// <summary>The provider searches are sent to; <c>null</c> only when there are none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FullText))]
    public partial IMediaSearchProvider? SelectedProvider { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FullText))]
    public partial string Query { get; set; } = "";

    /// <summary>
    /// The terms that narrow the search, other than its source: exact phrases, excluded ones and authors.
    /// They are written into the search field as text and shown there as buttons.
    /// </summary>
    public ObservableCollection<SearchTerm> Terms { get; } = [];

    /// <summary>
    /// The searches that were carried out, newest first. Typing searches as it goes, but a search only counts
    /// as carried out once it is committed to, with <see cref="CommitSearchAsync"/>.
    /// </summary>
    public ObservableCollection<RecentSearch> RecentSearches { get; } = [];

    /// <summary>The query the results are for. It lags behind <see cref="Query"/> while that is being typed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial string SearchedQuery { get; private set; } = "";

    /// <summary>Which page of the results is showing, counting from 1.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    public partial int Page { get; private set; } = 1;

    /// <summary>
    /// Whether there may be a page after this one. A source doesn't say how many results a search has, so a
    /// full page is taken to mean there are more, until the page after it comes back empty.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    public partial bool HasNextPage { get; private set; }

    private bool HasPreviousPage => Page > 1;

    // The last page of the current search, once the page after it has been asked for and had nothing on it.
    private int? _lastPage;

    // A different search starts again from its first page, with its last page unknown.
    partial void OnQueryChanged(string value) => StartFromFirstPage();

    partial void OnSelectedProviderChanged(IMediaSearchProvider? value) => StartFromFirstPage();

    private void StartFromFirstPage()
    {
        Page = 1;
        _lastPage = null;
    }

    /// <summary>
    /// The whole search as text, with the provider and the other terms written out:
    /// <c>source:youtube exact:"lofi beats" study</c>. This is what is copied, so pasting it back restores
    /// the provider and the terms as well as the words.
    /// </summary>
    public string FullText
    {
        get
        {
            var terms = new List<SearchTerm>();
            if (SelectedProvider is not null)
                terms.Add(SearchQuery.Source(SelectedProvider.Id));
            terms.AddRange(Terms);

            return new SearchQuery(Query.Trim(), terms).ToString();
        }
    }

    /// <summary>
    /// Replaces the search with one written as text, such as a pasted one. Its terms are applied and taken
    /// out, leaving only the words in <see cref="Query"/>.
    /// </summary>
    public void Replace(string text)
    {
        Terms.Clear();
        var hasTerms = ResolveTerms(text);
        if (!hasTerms)
            Query = text.Trim();
    }

    /// <summary>
    /// Applies any terms written in the text and takes them out of it, leaving the words in
    /// <see cref="Query"/>. Text without terms is left alone, so this can run while it is being typed.
    /// </summary>
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
        SearchQuery.SourceLabel => FindProvider(term.Value) is not null,
        SearchQuery.ExactLabel or SearchQuery.ExcludeLabel or SearchQuery.AuthorLabel => true,
        SearchQuery.DurationLabel or SearchQuery.PublishedLabel => SearchQuery.IsValid(term),
        // One end of a range written by itself is known if the range it makes is.
        _ => SearchQuery.RangeLabelOf(term.Label) is not null && SearchQuery.IsValid(SearchQuery.WithEnd(null, term))
    };

    private void Apply(SearchTerm term)
    {
        if (term.Label == SearchQuery.SourceLabel)
            SelectedProvider = FindProvider(term.Value);
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

    /// <summary>
    /// Adds a term to the search. A search can only have so many of one kind
    /// (<see cref="SearchQuery.MaxCount"/>), so past that the oldest of its kind makes way for it.
    /// </summary>
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

    /// <summary>
    /// The first page of the current search, whichever page is showing. The search field's list shows
    /// these, so it stays on the top results while the Search page is paged through.
    /// </summary>
    public ObservableCollection<MediaSearchResult> FirstPageResults { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial bool IsSearching { get; private set; }

    /// <summary>Explains an empty result list (nothing found, or an error); otherwise <c>null</c>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial string? Message { get; private set; }

    public bool HasMessage => Message is not null;

    /// <summary>The Search page's heading: the search's progress, which results are showing, or why there are none.</summary>
    public string Summary
    {
        get
        {
            if (SearchedQuery.Length == 0)
                return "Search";
            if (Message is not null)
                return Message;

            var source = SelectedProvider?.DisplayName;
            if (IsSearching)
                return $"Searching {source}";

            var first = (Page - 1) * PageSize + 1;
            var last = first + Results.Count - 1;
            return $"Results {first} to {last}";
        }
    }

    [RelayCommand(CanExecute = nameof(HasPreviousPage))]
    private Task PreviousPageAsync() => ShowPageAsync(Page - 1);

    [RelayCommand(CanExecute = nameof(HasNextPage))]
    private Task NextPageAsync() => ShowPageAsync(Page + 1);

    // Pages are only stepped through one at a time. A source can't jump to a page: it reads every result
    // before it to get there, and it doesn't say how many pages there are to jump among.
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
            RecentSearches.Add(ToRecentSearch(text));
    }

    /// <summary>
    /// Searches, and remembers the search as one that was carried out. This is for a search the user has
    /// asked for outright, with Enter or the Search button, not one made while they are still typing.
    /// </summary>
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
        var earlier = RecentSearches.FirstOrDefault(recent => string.Equals(recent.FullText, fullText, StringComparison.OrdinalIgnoreCase));
        if (earlier is not null)
            RecentSearches.Remove(earlier);

        RecentSearches.Insert(0, ToRecentSearch(fullText));
        while (RecentSearches.Count > RecentSearchCapacity)
            RecentSearches.RemoveAt(RecentSearches.Count - 1);
    }

    private RecentSearch ToRecentSearch(string fullText)
    {
        // Only the source is taken out, so the other terms stay in the words as they were written.
        var search = SearchQuery.Parse(fullText, term => term.Label == SearchQuery.SourceLabel);
        var source = search.Terms.LastOrDefault();
        var provider = source is null ? null : FindProvider(source.Value);
        return new RecentSearch(fullText, search.Text, provider?.Icon);
    }

    /// <summary>Searches the selected provider and replaces <see cref="Results"/> with the newest response.</summary>
    [RelayCommand]
    public async Task SearchAsync()
    {
        _running?.Cancel();
        _running = null;
        IsSearching = false;

        // The terms a provider applies are part of what is searched for, so they are part of what a search is
        // remembered by. Ranges of duration and date are applied here, to what the provider found, so
        // changing one shows the remembered results again through the new range.
        var search = new SearchQuery(Query.Trim(), Terms.ToList());
        var query = search.ToSearchText();
        var page = Page;
        Results.Clear();
        if (page == 1)
            FirstPageResults.Clear();
        Message = null;
        HasNextPage = false;
        SearchedQuery = query;

        if (query.Length == 0)
            return;

        var provider = SelectedProvider;
        if (provider is null)
        {
            Message = "There are no sources to search yet";
            return;
        }

        var remembered = _cache.Find(provider.Id, query, page);
        if (remembered is not null)
        {
            Show(remembered, search, provider, page);
            return;
        }

        var isPastTheEnd = false;

        // Each search owns its token source and disposes it when the search ends, however it ends.
        using var running = new CancellationTokenSource();
        _running = running;
        IsSearching = true;
        try
        {
            var skip = (page - 1) * PageSize;
            var found = await provider.SearchAsync(query, skip, PageSize, running.Token);
            // A newer search started while this one was waiting, so these results are out of date.
            if (running.IsCancellationRequested)
                return;

            // The page after a full last page has nothing on it. That is how the end of a search is found,
            // and the page before it is shown again instead.
            isPastTheEnd = found.Count == 0 && page > 1;
            if (isPastTheEnd)
                _lastPage = page - 1;
            else
                Show(found, search, provider, page);

            // An empty answer may only be a passing fault at the source, so it isn't remembered.
            if (found.Count > 0)
                _cache.Store(provider.Id, query, page, found);
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
            // Only the current search clears these. One that was replaced by a newer search leaves them
            // alone, or it would turn the busy indicator off while the newer search is still running.
            if (_running == running)
            {
                _running = null;
                IsSearching = false;
            }
        }

        if (isPastTheEnd)
            await ShowPageAsync(page - 1);
    }

    private void Show(IReadOnlyList<MediaSearchResult> found, SearchQuery search, IMediaSearchProvider provider, int page)
    {
        var withinRanges = found.Where(search.Matches).ToList();
        foreach (var result in withinRanges)
        {
            Results.Add(result);
            if (page == 1)
                FirstPageResults.Add(result);
        }

        if (found.Count == 0)
            Message = $"No results found in {provider.DisplayName}";
        else if (withinRanges.Count == 0)
            Message = "Nothing on this page is within the search's ranges";
        else
            Message = null;

        // Whether there is another page depends on what the provider found, not on how much of it is shown.
        var isLastPage = page == _lastPage;
        HasNextPage = found.Count == PageSize && !isLastPage;
        OnPropertyChanged(nameof(Summary));
    }
}
