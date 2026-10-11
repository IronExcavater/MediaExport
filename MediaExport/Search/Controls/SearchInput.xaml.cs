// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Controls;
using MediaExport.Converters;
using MediaExport.Core.Media.Providers;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
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

    private readonly ScalarTransition _fade = new() { Duration = VisibilityTransition.Duration };
    private readonly DispatcherQueueTimer _debounceTimer;
    private bool _isPasting;
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
            Clipboard.ContentChanged += Clipboard_ContentChanged;
            UpdateClipboardButton();

            VisibilityTransition.SetFade(FindDeleteButton(), true);
        };
        Unloaded += (_, _) => Clipboard.ContentChanged -= Clipboard_ContentChanged;
    }

    public SearchViewModel? ViewModel
    {
        get => (SearchViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    /// <summary>Raised when the Search button is pressed, to show the results as a page.</summary>
    public event EventHandler? Submitted;

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

    // TitleBar stops stretching its content once the content's desired width reaches the space available.
    // Asking only for MinWidth keeps the field stretched across the title bar.
    protected override Size MeasureOverride(Size availableSize)
    {
        var desired = base.MeasureOverride(availableSize);
        return new Size(MinWidth, desired.Height);
    }

    // A horizontal ScrollViewer gives its content only the width it asks for, so fieldContent is kept at
    // least as wide as the field and the query box fills the spare room.
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

    private static void OnViewModelChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var input = (SearchInput)sender;
        if (args.OldValue is SearchViewModel oldViewModel)
        {
            oldViewModel.FirstPageResults.CollectionChanged -= input.Results_CollectionChanged;
            oldViewModel.Terms.CollectionChanged -= input.Terms_CollectionChanged;
        }
        if (args.NewValue is SearchViewModel newViewModel)
        {
            newViewModel.FirstPageResults.CollectionChanged += input.Results_CollectionChanged;
            newViewModel.Terms.CollectionChanged += input.Terms_CollectionChanged;
        }
    }

    private void Results_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        // The first result of a new search arriving puts the list back at its top.
        var isFirstResult = args.Action == NotifyCollectionChangedAction.Add && args.NewStartingIndex == 0;
        if (isFirstResult)
            resultsList.ScrollIntoView(args.NewItems![0]);

        UpdateResultsPopup();
    }

    // The results are open while they are allowed here, there are some, and focus is in the query box or the results.
    private void UpdateResultsPopup()
    {
        var hasResults = ViewModel?.FirstPageResults.Count > 0;
        var hasFocus = IsFocusWithin(queryBox) || IsFocusWithin(resultsList);

        resultsPanel.Width = ActualWidth;
        resultsPopup.VerticalOffset = ActualHeight + ResultsGap;
        resultsPopup.IsOpen = IsResultsListEnabled && hasResults && hasFocus;
    }

    private bool IsFocusWithin(DependencyObject ancestor)
    {
        var focused = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        return IsWithin(focused, ancestor);
    }

    private static bool IsWithin(DependencyObject? element, DependencyObject ancestor)
    {
        while (element is not null)
        {
            if (element == ancestor)
                return true;

            element = VisualTreeHelper.GetParent(element);
        }
        return false;
    }

    // Also handles the results list losing focus.
    private void QueryBox_FocusChanged(object sender, RoutedEventArgs args)
    {
        var isQueryBoxFocused = queryBox.FocusState != FocusState.Unfocused;
        VisualStateManager.GoToState(this, isQueryBoxFocused ? "Focused" : "Unfocused", useTransitions: false);
        UpdateResultsPopup();
        UpdateClipboardButton();
    }

    // Presses on the results never get here, because a popup isn't part of the window's content.
    private void Window_PointerPressed(object sender, PointerRoutedEventArgs args)
    {
        var isOnThisControl = IsWithin(args.OriginalSource as DependencyObject, this);
        if (!isOnThisControl)
            resultsPopup.IsOpen = false;
    }

    // Pasted text may be a whole search with terms. The text box does the paste, and QueryBox_TextChanged
    // then resolves the terms.
    private void QueryBox_Paste(object sender, TextControlPasteEventArgs args) => _isPasting = true;

    private void QueryBox_TextChanged(object sender, TextChangedEventArgs args)
    {
        var text = queryBox.Text;
        if (_isPasting)
        {
            _isPasting = false;
            ViewModel?.Replace(text);
            queryBox.SelectionStart = queryBox.Text.Length;
        }
        else if (text.EndsWith(' '))
        {
            // A space ends a word, which is when a term typed as label:value is complete.
            var hadTerms = ViewModel?.ResolveTerms(text) == true;
            if (hadTerms)
                queryBox.SelectionStart = queryBox.Text.Length;
        }

        RestartDebounce();
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
        UpdateClipboardButton();
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

    private void TermBox_Edited(object? sender, SearchTerm edited)
    {
        var terms = ViewModel?.Terms;
        var index = IndexOfTerm(sender);
        if (terms is not null && index >= 0)
            terms[index] = edited;
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

    private void TermBox_EditingEnded(object? sender, EventArgs args) => FocusQueryBox();

    // Where a term's box's term is in the search, or -1 once it has gone from it.
    private int IndexOfTerm(object? termBox)
    {
        var term = ((SearchTermBox)termBox!).Term;
        var terms = ViewModel?.Terms;
        return term is null || terms is null ? -1 : terms.IndexOf(term);
    }

    private SearchTermBox? FindTermBox(SearchTerm term)
    {
        var index = ViewModel?.Terms.IndexOf(term) ?? -1;
        return index < 0 ? null : termsRepeater.TryGetElement(index) as SearchTermBox;
    }

    // At the start of the query box, Backspace removes the term before it and Left moves into that term to
    // edit it. This is a preview handler because TextBox handles both keys itself.
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
            FindTermBox(lastTerm)?.StartEditingAtEnd();
            args.Handled = true;
        }
    }

    // TextChanging is raised before the new text is drawn, so the clipboard button appears in the same frame
    // as the delete button instead of pushing it aside a moment later.
    private void QueryBox_TextChanging(TextBox sender, TextBoxTextChangingEventArgs args) => UpdateClipboardButton();

    private void QueryBox_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        switch (args.Key)
        {
            // Enter searches without waiting for the debounce, and completes a term still being typed.
            case VirtualKey.Enter:
                ViewModel?.ResolveTerms(queryBox.Text);
                _debounceTimer.Stop();
                ViewModel?.SearchCommand.Execute(null);
                args.Handled = true;
                break;

            case VirtualKey.Down when resultsPopup.IsOpen:
                var firstResult = resultsList.ContainerFromIndex(0) as Control;
                firstResult?.Focus(FocusState.Keyboard);
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

    // Opening the chosen result belongs here once there is somewhere to open it.
    private void ResultsList_ItemClick(object sender, ItemClickEventArgs args) => resultsPopup.IsOpen = false;

    private void Clipboard_ContentChanged(object? sender, object args) => DispatcherQueue.TryEnqueue(UpdateClipboardButton);

    // The clipboard button is hidden when there is neither a query to copy nor text to paste.
    private void UpdateClipboardButton()
    {
        var canCopy = HasSearch;
        var isUseful = canCopy || HasClipboardText();

        var action = canCopy ? "Copy to clipboard" : "Paste from clipboard";
        AutomationProperties.SetName(clipboardButton, action);
        ToolTipService.SetToolTip(clipboardButton, action);

        // The two icons cross-fade when the action changes while the button is showing. A button that is
        // only now appearing shows the right icon from the start.
        var wasHidden = clipboardButton.Visibility == Visibility.Collapsed;
        var iconFade = wasHidden ? null : _fade;
        copyIcon.OpacityTransition = iconFade;
        pasteIcon.OpacityTransition = iconFade;
        copyIcon.Opacity = canCopy ? 1 : 0;
        pasteIcon.Opacity = canCopy ? 0 : 1;

        clipboardButton.Visibility = isUseful ? Visibility.Visible : Visibility.Collapsed;
    }

    // DeleteButton is the name TextBox's template gives the button that clears the box.
    private Button FindDeleteButton()
    {
        var templateRoot = (FrameworkElement)VisualTreeHelper.GetChild(queryBox, 0);
        return (Button)templateRoot.FindName("DeleteButton");
    }

    private static bool HasClipboardText()
    {
        try
        {
            return Clipboard.GetContent().Contains(StandardDataFormats.Text);
        }
        catch (Exception exception) when (IsClipboardUnavailable(exception))
        {
            // The clipboard may still hold text, so pasting stays on offer.
            return true;
        }
    }

    // Whether there is anything to copy. The clipboard button copies when there is, and pastes when there isn't.
    private bool HasSearch => Is.NotBlank(queryBox.Text) || ViewModel?.Terms.Count > 0;

    private void ClipboardButton_Click(object sender, RoutedEventArgs args)
    {
        if (HasSearch)
            CopyQuery_Click(sender, args);
        else
            PasteQuery_Click(sender, args);
    }

    private void ClipboardMenu_Opening(object? sender, object args)
    {
        copyQueryItem.IsEnabled = HasSearch;
        pasteQueryItem.IsEnabled = HasClipboardText();
    }

    // The provider is copied with the query, as a term, so pasting restores both.
    private void CopyQuery_Click(object sender, RoutedEventArgs args)
    {
        var viewModel = ViewModel;
        if (viewModel is null)
            return;

        var package = new DataPackage();
        package.SetText(viewModel.FullText);
        Clipboard.SetContent(package);
    }

    private async void PasteQuery_Click(object sender, RoutedEventArgs args)
    {
        try
        {
            var content = Clipboard.GetContent();
            if (ViewModel is null || !content.Contains(StandardDataFormats.Text))
                return;

            var text = await content.GetTextAsync();
            ViewModel.Replace(text);
            FocusQueryBox();
        }
        catch (Exception exception) when (IsClipboardUnavailable(exception))
        {
            // The clipboard changed or was locked before it could be read, so nothing is pasted.
        }
    }

    // Thrown when another app has the clipboard locked, or this app is in the background.
    private static bool IsClipboardUnavailable(Exception exception) =>
        exception is COMException or UnauthorizedAccessException;

    private void Search_Click(object sender, RoutedEventArgs args) => Submitted?.Invoke(this, EventArgs.Empty);
}
