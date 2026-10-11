// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Controls;
using MediaExport.Converters;
using MediaExport.Core.Media.Providers;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Specialized;
using System.Runtime.InteropServices;
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
            oldViewModel.FirstPageResults.CollectionChanged -= input.Results_CollectionChanged;
        if (args.NewValue is SearchViewModel newViewModel)
            newViewModel.FirstPageResults.CollectionChanged += input.Results_CollectionChanged;
    }

    private void Results_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        // The first result of a new search arriving puts the list back at its top.
        var isFirstResult = args.Action == NotifyCollectionChangedAction.Add && args.NewStartingIndex == 0;
        if (isFirstResult)
            resultsList.ScrollIntoView(args.NewItems![0]);

        UpdateResultsPopup();
    }

    // The results are open while there are some and focus is in the query box or the results.
    private void UpdateResultsPopup()
    {
        var hasResults = ViewModel?.FirstPageResults.Count > 0;
        var hasFocus = IsFocusWithin(queryBox) || IsFocusWithin(resultsList);

        resultsPanel.Width = ActualWidth;
        resultsPopup.VerticalOffset = ActualHeight + ResultsGap;
        resultsPopup.IsOpen = hasResults && hasFocus;
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
        if (_isPasting)
        {
            _isPasting = false;
            ViewModel?.Replace(queryBox.Text);
            queryBox.SelectionStart = queryBox.Text.Length;
        }

        // Restarting the timer is what debounces the search.
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    // TextChanging is raised before the new text is drawn, so the clipboard button appears in the same frame
    // as the delete button instead of pushing it aside a moment later.
    private void QueryBox_TextChanging(TextBox sender, TextBoxTextChangingEventArgs args) => UpdateClipboardButton();

    private void QueryBox_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        switch (args.Key)
        {
            // Enter searches without waiting for the debounce.
            case VirtualKey.Enter:
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
        var canCopy = Is.NotBlank(queryBox.Text);
        var isUseful = canCopy || HasClipboardText();

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

    // The clipboard button copies when there is a query and pastes when there is none.
    private string ClipboardActionName(string? query) => Is.NotBlank(query) ? "Copy to clipboard" : "Paste from clipboard";

    private void ClipboardButton_Click(object sender, RoutedEventArgs args)
    {
        if (Is.NotBlank(queryBox.Text))
            CopyQuery_Click(sender, args);
        else
            PasteQuery_Click(sender, args);
    }

    private void ClipboardMenu_Opening(object? sender, object args)
    {
        copyQueryItem.IsEnabled = Is.NotBlank(queryBox.Text);
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
