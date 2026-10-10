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

/// <summary>Coordinates the shared search query, selected source, and current results.</summary>
public sealed partial class SearchViewModel : ObservableObject
{
    private const int Limit = 20;

    private readonly SearchCache _cache;

    // Cancels the search in progress when a newer one starts.
    private CancellationTokenSource? _running;

    public SearchViewModel(IEnumerable<IMediaSearchProvider> providers, SearchCache cache)
    {
        _cache = cache;
        Providers = providers.ToList();
        SelectedProvider = Providers.FirstOrDefault();
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
    /// The whole search as text, with the provider as a term: <c>source:youtube lofi beats</c>. This is what
    /// is copied, so pasting it back restores the provider as well as the words.
    /// </summary>
    public string FullText
    {
        get
        {
            var terms = new List<SearchTerm>();
            if (SelectedProvider is not null)
                terms.Add(SearchQuery.Source(SelectedProvider.Id));

            return new SearchQuery(Query.Trim(), terms).ToString();
        }
    }

    /// <summary>
    /// Replaces the search with one written as text, such as a pasted one. Its terms are applied and taken
    /// out, leaving only the words in <see cref="Query"/>.
    /// </summary>
    public void Replace(string text)
    {
        var search = SearchQuery.Parse(text, IsKnownTerm);
        foreach (var term in search.Terms)
            Apply(term);

        Query = search.Text;
    }

    public void Clear() => Query = string.Empty;

    // A new kind of term is added in these two methods: one says whether it is valid, the other applies it.
    private bool IsKnownTerm(SearchTerm term) => term.Label switch
    {
        SearchQuery.SourceLabel => FindProvider(term.Value) is not null,
        _ => false
    };

    private void Apply(SearchTerm term)
    {
        if (term.Label == SearchQuery.SourceLabel)
            SelectedProvider = FindProvider(term.Value);
    }

    private IMediaSearchProvider? FindProvider(string id) =>
        Providers.FirstOrDefault(provider => string.Equals(provider.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Results from the most recently completed search.</summary>
    public ObservableCollection<MediaSearchResult> Results { get; } = [];

    [ObservableProperty]
    public partial bool IsSearching { get; private set; }

    /// <summary>Explains an empty result list (nothing found, or an error); otherwise <c>null</c>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    public partial string? Message { get; private set; }

    public bool HasMessage => Message is not null;

    /// <summary>Searches the selected provider and replaces <see cref="Results"/> with the newest response.</summary>
    [RelayCommand]
    public async Task SearchAsync()
    {
        _running?.Cancel();
        _running = null;
        IsSearching = false;

        var query = Query.Trim();
        Results.Clear();
        Message = null;

        if (query.Length == 0)
            return;

        var provider = SelectedProvider;
        if (provider is null)
        {
            Message = "There are no sources to search yet";
            return;
        }

        var remembered = _cache.Find(provider.Id, query);
        if (remembered is not null)
        {
            Show(remembered, provider);
            return;
        }

        // Each search owns its token source and disposes it when the search ends, however it ends.
        using var running = new CancellationTokenSource();
        _running = running;
        IsSearching = true;
        try
        {
            var found = await provider.SearchAsync(query, Limit, running.Token);
            // A newer search started while this one was waiting, so these results are out of date.
            if (running.IsCancellationRequested)
                return;

            Show(found, provider);
            // An empty answer may only be a passing fault at the source, so it isn't remembered.
            if (found.Count > 0)
                _cache.Store(provider.Id, query, found);
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
    }

    private void Show(IReadOnlyList<MediaSearchResult> found, IMediaSearchProvider provider)
    {
        foreach (var result in found)
            Results.Add(result);
        Message = found.Count == 0 ? $"No results found in {provider.DisplayName}" : null;
    }
}
