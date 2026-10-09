// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Controls;
using MediaExport.Core.Media.Providers;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;

namespace MediaExport.Search;

/// <summary>Edits a search and tells its host when the Search button is pressed.</summary>
public sealed partial class SearchInput : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel),
        typeof(SearchViewModel),
        typeof(SearchInput),
        new PropertyMetadata(null));

    // How long the query must stay unchanged before a search starts. This debounces the search, so typing
    // a word doesn't start one search per letter.
    private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(400);

    private readonly DispatcherQueueTimer _debounceTimer;

    public SearchInput()
    {
        InitializeComponent();

        _debounceTimer = DispatcherQueue.CreateTimer();
        _debounceTimer.Interval = SearchDebounce;
        _debounceTimer.IsRepeating = false;
        _debounceTimer.Tick += (_, _) => ViewModel?.SearchCommand.Execute(null);

        // A search that is still waiting shouldn't start after the control has gone.
        Unloaded += (_, _) => _debounceTimer.Stop();
    }

    public SearchViewModel? ViewModel
    {
        get => (SearchViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    /// <summary>Raised when the Search button is pressed, to show the results as a page.</summary>
    public event EventHandler? Submitted;

    /// <summary>Moves keyboard focus to the text box.</summary>
    public void FocusQueryBox() => queryBox.Focus(FocusState.Keyboard);

    // Used by {x:Bind} to hide the provider button when there is no provider.
    private Visibility VisibleWith(object? value) => value is null ? Visibility.Collapsed : Visibility.Visible;

    // Reports MinWidth as the desired width, rather than the width it was last laid out at. The field is
    // stretched to fill the title bar either way, but TitleBar stops stretching its content (its "Compact"
    // state) once the content's desired width reaches the space available, which left the field stuck short.
    protected override Size MeasureOverride(Size availableSize)
    {
        var desired = base.MeasureOverride(availableSize);
        return new Size(MinWidth, desired.Height);
    }

    // A horizontal ScrollViewer gives its content only the width it asks for, so fieldContent is made at least
    // as wide as the field. The text box's star column then takes whatever room is spare.
    private void FieldScroll_SizeChanged(object sender, SizeChangedEventArgs args) => fieldContent.MinWidth = args.NewSize.Width;

    // MenuFlyout has no ItemsSource, so the providers are listed as it opens, the selected one in bold.
    private void ProviderMenu_Opening(object? sender, object args)
    {
        providerMenu.Items.Clear();
        var viewModel = ViewModel;
        if (viewModel is null)
            return;

        foreach (var provider in viewModel.Providers)
        {
            var item = new MenuFlyoutItem
            {
                Text = provider.DisplayName,
                Icon = IconView.Create(provider.Icon),
                FontWeight = provider == viewModel.SelectedProvider ? FontWeights.SemiBold : FontWeights.Normal
            };
            item.Click += (_, _) => viewModel.SelectedProvider = provider;
            providerMenu.Items.Add(item);
        }
    }

    // AutoSuggestBox closes its list when it loses focus and doesn't reopen it by itself, so coming back
    // to the box shows the results that are still there.
    private void QueryBox_GotFocus(object sender, RoutedEventArgs args)
    {
        var hasResults = ViewModel?.Results.Count > 0;
        if (hasResults)
            queryBox.IsSuggestionListOpen = true;
    }

    // Restarts the debounce timer on every change to the query.
    private void QueryBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        // Moving through the results with the arrow keys also reports a change, but isn't a new query.
        if (args.Reason == AutoSuggestionBoxTextChangeReason.SuggestionChosen)
            return;

        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    // Enter searches straight away instead of waiting for the debounce. Choosing a result from the list
    // doesn't do anything yet, because there is nowhere to open it. That belongs in this handler.
    private void QueryBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        _debounceTimer.Stop();
        if (args.ChosenSuggestion is null)
            ViewModel?.SearchCommand.Execute(null);
    }

    private async void ClipboardMenu_Opening(object? sender, object args)
    {
        var hasQuery = ViewModel?.FullText.Length > 0;
        copyQueryItem.IsEnabled = hasQuery;
        clearQueryItem.IsEnabled = hasQuery;
        pasteQueryItem.IsEnabled = false;
        try
        {
            var content = Clipboard.GetContent();
            var hasText = content.Contains(StandardDataFormats.Text);
            pasteQueryItem.IsEnabled = hasText && !string.IsNullOrWhiteSpace(await content.GetTextAsync());
        }
        catch (Exception exception) when (IsClipboardUnavailable(exception))
        {
            // Paste stays disabled until the menu is opened again.
        }
    }

    private void CopyQuery_Click(object sender, RoutedEventArgs args)
    {
        var query = ViewModel?.FullText;
        if (string.IsNullOrEmpty(query))
            return;

        var package = new DataPackage();
        package.SetText(query);
        Clipboard.SetContent(package);
    }

    private async void PasteQuery_Click(object sender, RoutedEventArgs args)
    {
        try
        {
            var content = Clipboard.GetContent();
            if (ViewModel is null || !content.Contains(StandardDataFormats.Text))
                return;

            var query = await content.GetTextAsync();
            if (string.IsNullOrWhiteSpace(query))
                return;

            ViewModel.Replace(query);
            FocusQueryBox();
        }
        catch (Exception exception) when (IsClipboardUnavailable(exception))
        {
            // The clipboard changed or was locked between opening the menu and choosing Paste, so nothing is pasted.
        }
    }

    // Windows reports a clipboard that another app has locked, or that this app isn't allowed to read while
    // it is in the background, with one of these two exceptions.
    private static bool IsClipboardUnavailable(Exception exception) =>
        exception is COMException or UnauthorizedAccessException;

    private void ClearQuery_Click(object sender, RoutedEventArgs args)
    {
        ViewModel?.Clear();
        FocusQueryBox();
    }

    private void Search_Click(object sender, RoutedEventArgs args) => Submitted?.Invoke(this, EventArgs.Empty);
}
