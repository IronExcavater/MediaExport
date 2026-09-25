// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MediaExport.Export;

public sealed partial class ExportPage : Page
{
    private ExportViewModel ViewModel { get; }

    public ExportPage()
    {
        InitializeComponent();
        ViewModel = App.Current.Services.GetRequiredService<ExportViewModel>();
        DataContext = ViewModel;
    }

    private void SearchBox_GotFocus(object sender, RoutedEventArgs args) =>
        ViewModel.ShowSuggestions();

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason is not AutoSuggestionBoxTextChangeReason.UserInput)
            return;

        ViewModel.SearchText = sender.Text;
        ViewModel.SearchCommand.Execute(null);
    }

    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (args.ChosenSuggestion is MediaSearchResult result)
            ViewModel.SelectResultCommand.Execute(result);
        else
        {
            ViewModel.SearchText = sender.Text;
            ViewModel.SearchCommand.Execute(null);
        }
    }

    private void SearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is MediaSearchResult result)
            ViewModel.SelectResultCommand.Execute(result);
    }

    private void HeroMediaView_PreviewRequested(object? sender, System.EventArgs args) =>
        ViewModel.ShowPreviewCommand.Execute(null);

    private void ErrorTip_CloseButtonClick(TeachingTip sender, object args) =>
        ViewModel.ErrorMessage = null;
}
