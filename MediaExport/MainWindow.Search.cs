// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using Microsoft.UI.Xaml.Input;
using System;

namespace MediaExport;

public sealed partial class MainWindow
{
    public void FocusSearch() => searchInput.FocusEditor();

    /// <summary>Asks every selected source for the search as it stands and opens the Search page.</summary>
    public void ShowResults()
    {
        Search.ResetSuggestions();
        Search.RememberSearch();
        GoTo("Search");
        _ = Search.QueryText.Length == 0 ? Results.LoadPopularAsync() : Results.SearchAsync(Search.QueryText);
    }

    /// <summary>Makes a recent search again.</summary>
    public void ShowResults(string search)
    {
        Search.Restore(search);
        ShowResults();
    }

    private void SearchInput_Submitted(object? sender, EventArgs args) => ShowResults();

    private void SearchInput_MediaChosen(object? sender, MediaSearchResult media) => Open(media);

    private void FocusSearch_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        FocusSearch();
        args.Handled = true;
    }
}
