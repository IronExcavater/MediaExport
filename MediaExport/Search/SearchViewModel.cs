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

    // Cancels the search in progress when a newer one starts.
    private CancellationTokenSource? _running;

    public SearchViewModel(IEnumerable<IMediaSearchProvider> providers)
    {
        Providers = providers.ToList();
        SelectedProvider = Providers.FirstOrDefault();
    }

    /// <summary>All search providers, in the order they were registered.</summary>
    public IReadOnlyList<IMediaSearchProvider> Providers { get; }

    /// <summary>The provider searches are sent to; <c>null</c> only when there are none.</summary>
    [ObservableProperty]
    public partial IMediaSearchProvider? SelectedProvider { get; set; }

    /// <summary>The text in the search box.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FullText))]
    public partial string Query { get; set; } = "";

    /// <summary>The query without surrounding whitespace, as copied to the clipboard.</summary>
    public string FullText => Query.Trim();

    public void Replace(string query) => Query = query.Trim();

    public void Clear() => Query = string.Empty;

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

            foreach (var result in found)
                Results.Add(result);
            Message = found.Count == 0 ? $"No results found in {provider.DisplayName}" : null;
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
}
