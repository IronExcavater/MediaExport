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

/// <summary>A source on the Search page: asked while it's picked, with how much it found.</summary>
public sealed partial class SourceFilter(IMediaSearchProvider provider) : ObservableObject
{
    public IMediaSearchProvider Provider => provider;

    /// <summary>Picked sources are the ones asked; they all start out picked.</summary>
    [ObservableProperty]
    public partial bool IsPicked { get; set; } = true;

    /// <summary>How many of the results on the page are its; nothing while there are none to count.</summary>
    [ObservableProperty]
    public partial string Count { get; set; } = string.Empty;
}

/// <summary>
/// The Search page: one search asked of several sources at once, its results filling in as each answers.
/// The page chooses the sources; the order comes from the title bar's sort, the same one its own suggestions use.
/// </summary>
public sealed partial class ResultsViewModel : ObservableObject
{
    // More from each source than a suggestion list has room for.
    private const int PerSource = 10;
    private static readonly TimeSpan TypingPause = TimeSpan.FromMilliseconds(500);

    private readonly SearchViewModel _search;
    private CancellationTokenSource? _running;
    private bool _isSwitching;

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

                OnPicksChanged();
            };
        }

        search.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(SearchViewModel.Sort) or nameof(SearchViewModel.Arrangement))
                _ = SearchAsync(Query);
        };
    }

    public IReadOnlyList<SourceFilter> Sources { get; }
    public ObservableCollection<MediaSearchResult> Results { get; } = [];

    public bool AreAllPicked => Sources.All(source => source.IsPicked);

    /// <summary>What the one button over all the sources does next: picks them all, or once they are, lets go of them all.</summary>
    public string PickAllLabel => AreAllPicked ? "Clear all" : "Select all";
    public string PickAllGlyph => AreAllPicked ? "\uE8E6" : "\uE8B3";

    /// <summary>What was last searched for.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Heading))]
    public partial string Query { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsSearching { get; private set; }

    /// <summary>How much was found and from how many sources, e.g. "42 results from 7 sources".</summary>
    [ObservableProperty]
    public partial string Summary { get; private set; } = string.Empty;

    /// <summary>Said in place of results: what to do first, that there's nothing, or what went wrong.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    public partial string? Message { get; private set; } = "Type in the search box above. Every source is searched at once, and what they have shows up here.";

    public bool HasMessage => Message is not null;
    public string Heading => Query.Length > 0 ? $"Results for “{Query}”" : "Search";

    /// <summary>Asks the picked sources; a newer search replaces one still under way.</summary>
    /// <param name="afterPause">Waits for typing to stop first, so each keystroke doesn't ask every source.</param>
    public async Task SearchAsync(string query, bool afterPause = false)
    {
        query = query.Trim();
        _running?.Cancel();
        if (query.Length == 0)
            return;

        var running = _running = new CancellationTokenSource();
        try
        {
            if (afterPause)
                await Task.Delay(TypingPause, running.Token);

            Query = query;
            Show([]);
            Message = null;
            IsSearching = true;

            IMediaSearchProvider[] asked = Sources.Where(source => source.IsPicked).Select(source => source.Provider).ToArray();
            if (asked.Length == 0)
            {
                IsSearching = false;
                Show([]);
                Message = "No source is picked. Pick one above, or select them all.";
                return;
            }

            var everything = new AllSearchProvider(asked, PerSource, _search.Arrangement);
            await foreach (IReadOnlyList<MediaSearchResult> soFar in everything.SearchEachAsync(query, _search.Sort, running.Token))
                Show(soFar);

            if (Results.Count == 0)
                Message = $"Nothing found for “{query}”.";
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (!running.IsCancellationRequested)
        {
            Message = $"Couldn't search. {exception.Message}";
        }
        finally
        {
            if (ReferenceEquals(_running, running))
                IsSearching = false;
        }
    }

    /// <summary>Picks every source, or if they all are, none, searching once rather than once per source.</summary>
    [RelayCommand]
    private void PickAll()
    {
        bool pick = !AreAllPicked;
        _isSwitching = true;
        foreach (SourceFilter source in Sources)
            source.IsPicked = pick;
        _isSwitching = false;
        OnPicksChanged();
    }

    private void OnPicksChanged()
    {
        OnPropertyChanged(nameof(AreAllPicked));
        OnPropertyChanged(nameof(PickAllLabel));
        OnPropertyChanged(nameof(PickAllGlyph));
        _ = SearchAsync(Query);
    }

    // Results are moved into their new order in place, so those already on screen aren't all redrawn as more arrive.
    private void Show(IReadOnlyList<MediaSearchResult> found)
    {
        for (int index = 0; index < found.Count; index++)
        {
            int existing = Results.IndexOf(found[index]);
            if (existing < 0)
                Results.Insert(index, found[index]);
            else if (existing != index)
                Results.Move(existing, index);
        }

        while (Results.Count > found.Count)
            Results.RemoveAt(Results.Count - 1);

        foreach (SourceFilter source in Sources)
        {
            int count = found.Count(result => result.ProviderId == source.Provider.Id);
            source.Count = count > 0 ? $"{count}" : string.Empty;
        }

        int sources = found.Select(result => result.ProviderId).Distinct().Count();
        Summary = found.Count == 0 ? string.Empty
            : $"{found.Count} {(found.Count == 1 ? "result" : "results")} from {sources} {(sources == 1 ? "source" : "sources")}";
    }
}
