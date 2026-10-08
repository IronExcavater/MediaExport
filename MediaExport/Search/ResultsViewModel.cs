// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaExport.Core.Media.Models;
using MediaExport.Core.Media.Providers;
using MediaExport.Core.Settings;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MediaExport.Search;

/// <summary>A source that can be left out of a search.</summary>
public sealed partial class SourceFilter(IMediaSearchProvider provider) : ObservableObject
{
    public IMediaSearchProvider Provider => provider;

    [ObservableProperty]
    public partial bool IsOn { get; set; } = true;
}

/// <summary>
/// The Search page: one search asked of every source at once, its results filling in as each answers.
/// The page's own controls say which sources are asked, what kind of media is shown and in what order.
/// </summary>
public sealed partial class ResultsViewModel : ObservableObject
{
    // More from each source than a suggestion list has room for.
    private const int PerSource = 10;
    private static readonly TimeSpan TypingPause = TimeSpan.FromMilliseconds(500);

    private readonly SettingsStore _settings;
    private IReadOnlyList<MediaSearchResult> _found = [];
    private CancellationTokenSource? _search;
    private bool _isChangingSources;

    public ResultsViewModel(IEnumerable<IMediaSearchProvider> providers, SettingsStore settings)
    {
        _settings = settings;
        Sources = providers.Select(provider => new SourceFilter(provider)).ToArray();
        foreach (SourceFilter source in Sources)
        {
            source.PropertyChanged += (_, _) =>
            {
                if (!_isChangingSources)
                    _ = SearchAsync(Query);
            };
        }

        SortIndex = (int)settings.Current.Sort;
    }

    public IReadOnlyList<SourceFilter> Sources { get; }
    public ObservableCollection<MediaSearchResult> Results { get; } = [];

    /// <summary>What was last searched for.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Heading))]
    public partial string Query { get; private set; } = string.Empty;

    /// <summary>Which results come first, as a position in <see cref="SearchSort"/>.</summary>
    [ObservableProperty]
    public partial int SortIndex { get; set; }

    /// <summary>What's shown of what was found: everything, only video, or only audio.</summary>
    [ObservableProperty]
    public partial int KindIndex { get; set; }

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
    public string Heading => Query.Length > 0 ? $"Results for “{Query}”" : "Search every source";

    /// <summary>Asks every source that's switched on; a newer search replaces one still under way.</summary>
    /// <param name="afterPause">Waits for typing to stop first, so each keystroke doesn't ask every source.</param>
    public async Task SearchAsync(string query, bool afterPause = false)
    {
        query = query.Trim();
        _search?.Cancel();
        if (query.Length == 0)
            return;

        var search = _search = new CancellationTokenSource();
        try
        {
            if (afterPause)
                await Task.Delay(TypingPause, search.Token);

            var sort = (SearchSort)SortIndex;
            Query = query;
            _found = [];
            Show();
            Message = null;
            IsSearching = true;

            IMediaSearchProvider[] sources = Sources.Where(source => source.IsOn).Select(source => source.Provider).ToArray();
            await foreach (IReadOnlyList<MediaSearchResult> soFar in new AllSearchProvider(sources, PerSource).SearchEachAsync(query, sort, search.Token))
            {
                _found = soFar;
                Show();
            }

            if (_found.Count == 0)
                Message = sources.Length == 0 ? "Every source is switched off. Switch one on above to search it." : $"Nothing found for “{query}”.";
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (!search.IsCancellationRequested)
        {
            Message = $"Couldn't search. {exception.Message}";
        }
        finally
        {
            if (ReferenceEquals(_search, search))
                IsSearching = false;
        }
    }

    /// <summary>Switches every source on or off in one go, searching once afterwards rather than once per source.</summary>
    [RelayCommand]
    private void SetAllSources(string on)
    {
        _isChangingSources = true;
        foreach (SourceFilter source in Sources)
            source.IsOn = bool.Parse(on);
        _isChangingSources = false;
        _ = SearchAsync(Query);
    }

    partial void OnSortIndexChanged(int value)
    {
        if (_settings.Current.Sort == (SearchSort)value)
            return;

        _settings.Current.Sort = (SearchSort)value;
        _settings.Save();
        _ = SearchAsync(Query);
    }

    // Narrowing by kind only changes what's shown of what was found; nothing is asked again.
    partial void OnKindIndexChanged(int value) => Show();

    // Results are moved into their new order in place, so those already on screen aren't all redrawn as more arrive.
    private void Show()
    {
        MediaSearchResult[] shown = _found
            .Where(result => KindIndex == 0 || result.Kind == (KindIndex == 1 ? MediaKind.Video : MediaKind.Audio))
            .ToArray();
        for (int index = 0; index < shown.Length; index++)
        {
            int existing = Results.IndexOf(shown[index]);
            if (existing < 0)
                Results.Insert(index, shown[index]);
            else if (existing != index)
                Results.Move(existing, index);
        }

        while (Results.Count > shown.Length)
            Results.RemoveAt(Results.Count - 1);

        int sources = shown.Select(result => result.ProviderId).Distinct().Count();
        Summary = shown.Length == 0 ? string.Empty
            : $"{shown.Length} {(shown.Length == 1 ? "result" : "results")} from {sources} {(sources == 1 ? "source" : "sources")}";
        if (_found.Count > 0 && Query.Length > 0)
            Message = shown.Length == 0 ? "Nothing of that kind was found. Show everything to see the rest." : null;
    }
}
