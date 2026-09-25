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

namespace MediaExport.Export;

public partial class ExportViewModel : ObservableObject, IDisposable
{
    private CancellationTokenSource? _searchCancellation;

    public ExportViewModel(IEnumerable<IMediaSearchProvider> searchProviders)
    {
        Providers = searchProviders.ToArray();
        SelectedProvider = Providers.FirstOrDefault();
    }

    public void Dispose()
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
    }

    public IReadOnlyList<IMediaSearchProvider> Providers { get; }
    public ObservableCollection<MediaSearchResult> Suggestions { get; } = [];

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
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
    public partial MediaSearchResult? SelectedResult { get; set; }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasSelectedResult => SelectedResult is not null;

    partial void OnSearchTextChanged(string value) => IsSuggestionListOpen = false;

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (SelectedProvider is null || string.IsNullOrWhiteSpace(SearchText))
            return;

        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        var searchCancellation = new CancellationTokenSource();
        _searchCancellation = searchCancellation;
        CancellationToken token = searchCancellation.Token;

        IsSearching = true;
        IsSuggestionListOpen = false;
        ErrorMessage = null;
        SelectedResult = null;

        try
        {
            IReadOnlyList<MediaSearchResult> results = await SelectedProvider.SearchAsync(SearchText, token);

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
        SelectedResult = result;
        IsSuggestionListOpen = false;
    }
}
