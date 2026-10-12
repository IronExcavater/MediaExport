// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Controls;
using MediaExport.Converters;
using MediaExport.Core.Media.Models;
using MediaExport.Core.Media.Providers;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.UI.Core;
using VirtualKey = Windows.System.VirtualKey;

namespace MediaExport.Search.Controls;

/// <summary>Edits a search and tells its host when the Search button is pressed.</summary>
public sealed partial class SearchInput : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel),
        typeof(SearchViewModel),
        typeof(SearchInput),
        new PropertyMetadata(null, OnViewModelChanged));

    // The query must stay unchanged this long before a search starts, so typing a word is one search.
    private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(400);

    private const double ResultsGap = 2;

    private readonly DispatcherQueueTimer _debounceTimer;
    private readonly PointerEventHandler _windowPressed;

    public SearchInput()
    {
        InitializeComponent();

        _debounceTimer = DispatcherQueue.CreateTimer();
        _debounceTimer.Interval = SearchDebounce;
        _debounceTimer.IsRepeating = false;
        _debounceTimer.Tick += (_, _) => ViewModel?.SearchCommand.Execute(null);

        // A search that is still waiting shouldn't start after the control has gone.
        Unloaded += (_, _) => _debounceTimer.Stop();

        // Presses anywhere in the window close the results, including ones a control has already handled.
        _windowPressed = new PointerEventHandler(Window_PointerPressed);
        Loaded += (_, _) => XamlRoot.Content.AddHandler(PointerPressedEvent, _windowPressed, handledEventsToo: true);
        Unloaded += (_, _) => XamlRoot?.Content?.RemoveHandler(PointerPressedEvent, _windowPressed);

        SizeChanged += (_, _) => UpdateResultsPopup();

        Loaded += (_, _) =>
        {
            UpdateFieldButtons();
            HideQueryBoxDeleteButton();

            // Fades added in the XAML would also run on the icons' first opacities, so they are added after.
            searchIcon.OpacityTransition = new ScalarTransition { Duration = VisibilityTransition.Duration };
            seeAllIcon.OpacityTransition = new ScalarTransition { Duration = VisibilityTransition.Duration };
        };
    }

    public SearchViewModel? ViewModel
    {
        get => (SearchViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    /// <summary>Raised when the Search button is pressed, to show the results as a page.</summary>
    public event EventHandler? Submitted;

    /// <summary>Raised when a result listed under the field is chosen, to open it.</summary>
    public event EventHandler<MediaSearchResult>? ResultChosen;

    /// <summary>Raised when a link is pasted or entered in place of a search, to open what it is to.</summary>
    public event EventHandler<Uri>? LinkSubmitted;

    public void FocusQueryBox() => queryBox.Focus(FocusState.Keyboard);

    /// <summary>Whether the results may be listed under the field. A page that shows them itself turns this off.</summary>
    public bool IsResultsListEnabled
    {
        get;
        set
        {
            field = value;
            UpdateResultsPopup();
        }
    } = true;

    // Asking only for MinWidth keeps TitleBar stretching the field, which it stops once content wants all the space.
    protected override Size MeasureOverride(Size availableSize)
    {
        var desired = base.MeasureOverride(availableSize);
        return new Size(MinWidth, desired.Height);
    }

    // A horizontal ScrollViewer gives content only the width it asks for, so fieldContent is kept as wide as the field.
    private void FieldScroll_SizeChanged(object sender, SizeChangedEventArgs args) => fieldContent.MinWidth = args.NewSize.Width;

    private void ProviderFlyout_Opening(object? sender, object args)
    {
        // The pointer is still on the button, so its tooltip would come up over the open flyout.
        ToolTipService.SetToolTip(providerButton, null);
        providerPicker.Refresh();
    }

    private void ProviderFlyout_Opened(object? sender, object args) => providerPicker.FocusChosenRow();

    private void ProviderFlyout_Closed(object? sender, object args) => ShowProviderIcons();

    // Choosing one provider closes the flyout, as choosing from a menu does.
    private void ProviderPicker_OneChosen(object? sender, EventArgs args) => providerFlyout.Hide();

    // The ways of ranking, listed as the menu opens with the one in use in bold.
    private void SortMenu_Opening(object? sender, object args)
    {
        sortMenu.Items.Clear();
        var viewModel = ViewModel;
        if (viewModel is null)
            return;

        (SearchSort Sort, string Name, string Glyph)[] choices =
        [
            (SearchSort.Relevance, "Most relevant", "\uE734"),
            (SearchSort.Views, "Most viewed", "\uE7B3")
        ];
        foreach (var choice in choices)
        {
            var item = new MenuFlyoutItem
            {
                Text = choice.Name,
                Icon = new FontIcon { Glyph = choice.Glyph },
                FontWeight = choice.Sort == viewModel.Sort ? FontWeights.SemiBold : FontWeights.Normal
            };
            item.Click += (_, _) => viewModel.Sort = choice.Sort;
            sortMenu.Items.Add(item);
        }
    }

    // The button shows the chosen providers' icons, and its tooltip names them.
    private void ShowProviderIcons()
    {
        var providers = ViewModel?.SelectedProviders.ToList() ?? [];
        providerIcons.Icons = providers.Select(provider => provider.Icon);

        // The tooltip stays off while the flyout is open, where the providers are named anyway.
        var names = string.Join(", ", providers.Select(provider => provider.DisplayName));
        var hasTooltip = names.Length > 0 && !providerFlyout.IsOpen;
        ToolTipService.SetToolTip(providerButton, hasTooltip ? names : null);
    }

    private void SelectedProviders_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        ShowProviderIcons();
        RestartDebounce();
    }

    private static void OnViewModelChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var input = (SearchInput)sender;
        if (args.OldValue is SearchViewModel oldViewModel)
        {
            oldViewModel.FirstPageResults.CollectionChanged -= input.Results_CollectionChanged;
            oldViewModel.Terms.CollectionChanged -= input.Terms_CollectionChanged;
            oldViewModel.PropertyChanged -= input.ViewModel_PropertyChanged;
            oldViewModel.RecentSearches.CollectionChanged -= input.Results_CollectionChanged;
            oldViewModel.SelectedProviders.CollectionChanged -= input.SelectedProviders_CollectionChanged;
        }
        if (args.NewValue is SearchViewModel newViewModel)
        {
            newViewModel.FirstPageResults.CollectionChanged += input.Results_CollectionChanged;
            newViewModel.Terms.CollectionChanged += input.Terms_CollectionChanged;
            newViewModel.PropertyChanged += input.ViewModel_PropertyChanged;
            newViewModel.RecentSearches.CollectionChanged += input.Results_CollectionChanged;
            newViewModel.SelectedProviders.CollectionChanged += input.SelectedProviders_CollectionChanged;

            // With no providers there is nothing for the button to show or offer.
            var hasProviders = VisibleWhen.True(newViewModel.Providers.Count > 0);
            input.providerButton.Visibility = hasProviders;
            input.providerDivider.Visibility = hasProviders;
            input.ShowProviderIcons();
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SearchViewModel.IsSearching))
            UpdateResultsPopup();

        // A different ranking is a different search, made after the same pause as a change to the query.
        if (args.PropertyName == nameof(SearchViewModel.Sort))
            RestartDebounce();
    }

    private void Results_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        // The first result of a new search arriving puts the list back at its top.
        var isResult = args.NewItems?[0] is MediaSearchResult;
        var isFirstResult = isResult && args.Action == NotifyCollectionChangedAction.Add && args.NewStartingIndex == 0;
        if (isFirstResult)
            resultsList.ScrollIntoView(args.NewItems![0]);

        UpdateResultsPopup();
    }

    // Whether the popup under the field is listing results, as opposed to recent searches.
    private bool _isListingResults;

    // Lists recent searches while the field is empty, otherwise the results, unless the page lists them itself.
    private void UpdateResultsPopup()
    {
        var viewModel = ViewModel;
        var hasResults = viewModel?.FirstPageResults.Count > 0;
        var isLoading = viewModel?.IsSearching == true && !hasResults;
        var hasRecents = viewModel?.RecentSearches.Count > 0;

        // The app focuses the field as it opens, which shouldn't open recent searches; going to the field does.
        var isFieldChosen = queryBox.FocusState is FocusState.Pointer or FocusState.Keyboard;
        var isInPopup = IsFocusWithin(resultsList) || IsFocusWithin(recentsList);
        var hasFocus = IsFocusWithin(queryBox) || isInPopup;

        var listsRecents = !HasSearch && hasRecents && (isFieldChosen || isInPopup);
        var listsResults = HasSearch && IsResultsListEnabled && (hasResults || isLoading) && hasFocus;
        _isListingResults = listsResults;

        recentsList.Visibility = VisibleWhen.True(listsRecents);
        resultsFade.Visibility = VisibleWhen.True(listsResults);
        resultsSkeleton.Visibility = VisibleWhen.True(listsResults && isLoading);

        resultsPanel.Width = ActualWidth;
        resultsPopup.VerticalOffset = ActualHeight + ResultsGap;
        resultsPopup.IsOpen = listsRecents || listsResults;
        UpdateSearchButton();
    }

    private bool IsFocusWithin(DependencyObject ancestor)
    {
        var focused = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        return VisualTree.IsWithin(focused, ancestor);
    }

    // Also handles the results list losing focus.
    private void QueryBox_FocusChanged(object sender, RoutedEventArgs args)
    {
        var isQueryBoxFocused = queryBox.FocusState != FocusState.Unfocused;
        VisualStateManager.GoToState(this, isQueryBoxFocused ? "Focused" : "Unfocused", useTransitions: false);

        // A term typed last has no space after it to complete it, so leaving the box does.
        if (!isQueryBoxFocused)
            ViewModel?.ResolveTerms(queryBox.Text);

        UpdateResultsPopup();
        UpdateFieldButtons();
    }

    // Presses on the results never get here, because a popup isn't part of the window's content.
    private void Window_PointerPressed(object sender, PointerRoutedEventArgs args)
    {
        var isOnThisControl = VisualTree.IsWithin(args.OriginalSource as DependencyObject, this);
        if (!isOnThisControl)
            resultsPopup.IsOpen = false;
    }

    // Pasted text may be a whole search with terms. The box would show them as text for a moment before they
    // resolved, so the paste is done here and the box only ever gets the words that are left.
    private async void QueryBox_Paste(object sender, TextControlPasteEventArgs args)
    {
        args.Handled = true;
        var selectionStart = queryBox.SelectionStart;
        var selectionLength = queryBox.SelectionLength;
        var clipboard = Clipboard.GetContent();
        if (!clipboard.Contains(StandardDataFormats.Text))
            return;

        var pasted = await clipboard.GetTextAsync();
        var oneLine = pasted.ReplaceLineEndings(" ");
        if (TryOpenLink(oneLine))
            return;

        var text = queryBox.Text.Remove(selectionStart, selectionLength).Insert(selectionStart, oneLine);
        ViewModel?.Replace(text);
        queryBox.SelectionStart = queryBox.Text.Length;
    }

    private void QueryBox_TextChanged(object sender, TextChangedEventArgs args)
    {
        // A space ends a word, which is when a term typed as label:value is complete.
        var text = queryBox.Text;
        var hadTerms = text.EndsWith(' ') && ViewModel?.ResolveTerms(text) == true;
        if (hadTerms)
            queryBox.SelectionStart = queryBox.Text.Length;

        RestartDebounce();
    }

    // Searches now, as one that was asked for, instead of after the pause that follows typing. A term that is
    // still being typed is completed first.
    private void CommitSearch()
    {
        ViewModel?.ResolveTerms(queryBox.Text);
        _debounceTimer.Stop();
        ViewModel?.CommitSearchCommand.Execute(null);
    }

    // A link is opened instead of being searched for. Returns whether the text was one.
    private bool TryOpenLink(string text)
    {
        var link = SearchQuery.ReadLink(text);
        if (link is null)
            return false;

        resultsPopup.IsOpen = false;
        LinkSubmitted?.Invoke(this, link);
        return true;
    }

    // Restarting the timer is what debounces the search.
    private void RestartDebounce()
    {
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    // The terms that have been added but whose boxes the repeater hasn't made yet. They slide in when it does.
    private readonly HashSet<SearchTerm> _termsToSlideIn = [];

    private void Terms_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        // An edited term is replaced, not added, so it stays where it is without sliding.
        if (args.Action == NotifyCollectionChangedAction.Add)
        {
            foreach (SearchTerm added in args.NewItems!)
                _termsToSlideIn.Add(added);
        }

        RestartDebounce();
        UpdateFieldButtons();
    }

    // The term that was just added from the menu, whose value is to be typed next.
    private SearchTerm? _termToEdit;

    private void AddTerm_Click(object sender, RoutedEventArgs args)
    {
        var viewModel = ViewModel;
        if (viewModel is null)
            return;

        var item = (MenuFlyoutItem)sender;
        var label = (string)item.Tag;

        // A kind of term that a search has only one of is edited where it is, instead of being replaced.
        var existing = viewModel.Terms.FirstOrDefault(term => term.Label == label);
        var isOnlyOne = SearchQuery.MaxCount(label) == 1;
        if (existing is not null && isOnlyOne)
        {
            FindTermBox(existing)?.StartEditing();
            return;
        }

        _termToEdit = new SearchTerm(label, string.Empty);
        viewModel.AddTerm(_termToEdit);
    }

    // A term's box doesn't exist until the repeater has made it, which is when a new term can take focus.
    private void TermsRepeater_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        var term = ViewModel?.Terms[args.Index];
        var termBox = (SearchTermBox)args.Element;
        if (term is null)
            return;

        var isNew = _termsToSlideIn.Remove(term);
        if (isNew)
            termBox.SlideIn();

        if (term == _termToEdit)
        {
            _termToEdit = null;
            DispatcherQueue.TryEnqueue(termBox.StartEditing);
        }
    }

    private async void TermBox_Edited(object? sender, SearchTerm edited)
    {
        var termBox = (SearchTermBox)sender!;
        var term = termBox.Term;
        if (term is null)
            return;

        if (SearchQuery.IsValid(edited))
        {
            // Replacing a term makes its box again, so a term that was selected is selected again.
            var wasSelected = termBox.IsSelected;
            ViewModel?.ReplaceTerm(term, edited);
            if (wasSelected)
                DispatcherQueue.TryEnqueue(() => FindTermBox(edited)?.Select());
            return;
        }

        // An unreadable value is given up, so the term goes back to its old value, or goes altogether if it was new.
        var isNew = term.Value.Length == 0;
        if (isNew)
            await RemoveTermAsync(term);
        else
            termBox.Term = term;
    }

    private async void TermBox_Removed(object? sender, EventArgs args)
    {
        var term = ((SearchTermBox)sender!).Term;
        if (term is null)
            return;

        FocusQueryBox();
        await RemoveTermAsync(term);
    }

    // The term slides out of view before it is taken off the search, which is what removes its box.
    private async Task RemoveTermAsync(SearchTerm term)
    {
        var termBox = FindTermBox(term);
        if (termBox is not null)
            await termBox.SlideOutAsync();

        ViewModel?.Terms.Remove(term);
    }

    // On a selected term the arrows move to what is beside it, and Backspace and Delete remove it.
    private async void TermsRepeater_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        var terms = ViewModel?.Terms;
        var selected = terms?.FirstOrDefault(term => FindTermBox(term)?.IsSelected == true);
        if (terms is null || selected is null)
            return;

        var index = terms.IndexOf(selected);
        switch (args.Key)
        {
            case VirtualKey.Left when index > 0:
                args.Handled = true;
                SelectTermOrQueryBox(index - 1);
                break;

            case VirtualKey.Right:
                args.Handled = true;
                SelectTermOrQueryBox(index + 1);
                break;

            // The term that takes the removed one's place is selected next.
            case VirtualKey.Back or VirtualKey.Delete:
                args.Handled = true;
                await RemoveTermAsync(selected);
                SelectTermOrQueryBox(index);
                break;
        }
    }

    // The query box comes after the last term.
    private void SelectTermOrQueryBox(int index)
    {
        var terms = ViewModel?.Terms;
        var isTerm = terms is not null && index < terms.Count;
        var termBox = isTerm ? FindTermBox(terms![index]) : null;
        if (termBox is null)
            FocusQueryBox();
        else
            termBox.Select();
    }

    private SearchTermBox? FindTermBox(SearchTerm term)
    {
        var index = ViewModel?.Terms.IndexOf(term) ?? -1;
        return index < 0 ? null : termsRepeater.TryGetElement(index) as SearchTermBox;
    }

    // At the query box's start, Backspace removes the last term and Left selects it. Preview, as TextBox handles both.
    private async void QueryBox_PreviewKeyDown(object sender, KeyRoutedEventArgs args)
    {
        var terms = ViewModel?.Terms;
        var isAtStart = queryBox.SelectionStart == 0 && queryBox.SelectionLength == 0;
        if (!isAtStart || terms is null || terms.Count == 0)
            return;

        var lastTerm = terms[^1];
        if (args.Key == VirtualKey.Back)
        {
            args.Handled = true;
            await RemoveTermAsync(lastTerm);
        }
        else if (args.Key == VirtualKey.Left)
        {
            FindTermBox(lastTerm)?.Select();
            args.Handled = true;
        }
    }

    // TextChanging is raised before the text is drawn, so the clipboard and delete buttons change in the same frame.
    private void QueryBox_TextChanging(TextBox sender, TextBoxTextChangingEventArgs args) => UpdateFieldButtons();

    private void QueryBox_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        switch (args.Key)
        {
            case VirtualKey.Enter:
                if (!TryOpenLink(queryBox.Text))
                    CommitSearch();

                args.Handled = true;
                break;

            // Down moves into whichever list the popup is showing.
            case VirtualKey.Down when resultsPopup.IsOpen:
                var list = _isListingResults ? resultsList : recentsList;
                var firstItem = list.ContainerFromIndex(0) as Control;
                firstItem?.Focus(FocusState.Keyboard);
                args.Handled = true;
                break;

            case VirtualKey.Escape:
                resultsPopup.IsOpen = false;
                args.Handled = true;
                break;
        }
    }

    // Escape in the results returns to the query box.
    private void ResultsList_PreviewKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Escape)
            return;

        resultsPopup.IsOpen = false;
        FocusQueryBox();
        args.Handled = true;
    }

    private void ResultsList_ItemClick(object sender, ItemClickEventArgs args)
    {
        // Choosing a result is acting on the search, which makes it one that was carried out.
        CommitSearch();
        resultsPopup.IsOpen = false;
        ResultChosen?.Invoke(this, (MediaSearchResult)args.ClickedItem);
    }

    private void RecentsList_ItemClick(object sender, ItemClickEventArgs args)
    {
        var recent = (RecentSearch)args.ClickedItem;
        ViewModel?.Replace(recent.FullText);
        CommitSearch();

        FocusQueryBox();
        queryBox.SelectionStart = queryBox.Text.Length;
    }

    private void RemoveRecent_Click(object sender, RoutedEventArgs args)
    {
        var button = (Button)sender;
        var recent = (RecentSearch)button.Tag;
        ViewModel?.RecentSearches.Remove(recent);
    }

    // With a search, the delete button empties it and the clipboard button copies it; without, it pastes.
    private void UpdateFieldButtons()
    {
        var hasSearch = HasSearch;
        deleteButton.Visibility = VisibleWhen.True(hasSearch);
        clipboardButton.CanCopy = hasSearch;
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs args)
    {
        ViewModel?.Replace(string.Empty);
        FocusQueryBox();
    }

    // TextBox shows its own delete button, so it gets no width and no opacity; either alone leaves part showing.
    private void HideQueryBoxDeleteButton()
    {
        var templateRoot = (FrameworkElement)VisualTreeHelper.GetChild(queryBox, 0);
        var ownDeleteButton = (Button)templateRoot.FindName("DeleteButton");
        ownDeleteButton.MinWidth = 0;
        ownDeleteButton.Width = 0;
        ownDeleteButton.Opacity = 0;
    }

    // Whether the field holds a search, which is words in the query box or any terms.
    private bool HasSearch => Is.NotBlank(queryBox.Text) || ViewModel?.Terms.Count > 0;

    private void ClipboardButton_Pasted(object? sender, string text)
    {
        if (TryOpenLink(text))
            return;

        ViewModel?.Replace(text);
        FocusQueryBox();
    }

    private void Search_Click(object sender, RoutedEventArgs args)
    {
        if (TryOpenLink(queryBox.Text))
            return;

        ViewModel?.ResolveTerms(queryBox.Text);
        Submitted?.Invoke(this, EventArgs.Empty);
    }

    private void ResultsPopup_OpenChanged(object? sender, object args) => UpdateSearchButton();

    // While results are listed under the field, the Search button cross-fades to an icon for seeing them all.
    private void UpdateSearchButton()
    {
        var isListed = resultsPopup.IsOpen && _isListingResults;
        var action = isListed ? "See all results" : "Search";
        searchIcon.Opacity = isListed ? 0 : 1;
        seeAllIcon.Opacity = isListed ? 1 : 0;
        AutomationProperties.SetName(searchButton, action);
        ToolTipService.SetToolTip(searchButton, action);
    }
}
