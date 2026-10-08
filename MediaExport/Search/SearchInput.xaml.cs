// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.ComponentModel;
using Windows.System;

namespace MediaExport.Search;

/// <summary>A compact editor that presents ordinary words, structured terms and search actions as one field.</summary>
public sealed partial class SearchInput : UserControl
{
    private TextBox? _textBox;

    public SearchInput() => InitializeComponent();

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(SearchViewModel), typeof(SearchInput),
        new PropertyMetadata(null, (control, args) => ((SearchInput)control).SetViewModel((SearchViewModel?)args.OldValue, (SearchViewModel?)args.NewValue)));

    public static readonly DependencyProperty ResultsProperty = DependencyProperty.Register(
        nameof(Results), typeof(ResultsViewModel), typeof(SearchInput),
        new PropertyMetadata(null, (control, args) => ((SearchInput)control).SetResults((ResultsViewModel?)args.OldValue, (ResultsViewModel?)args.NewValue)));

    public SearchViewModel ViewModel
    {
        get => (SearchViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public ResultsViewModel Results
    {
        get => (ResultsViewModel)GetValue(ResultsProperty);
        set => SetValue(ResultsProperty, value);
    }

    public event EventHandler? Submitted;
    public event EventHandler<MediaSearchResult>? MediaChosen;

    public void FocusEditor()
    {
        editor.Focus(FocusState.Keyboard);
        if (_textBox is not null)
            _textBox.SelectionStart = _textBox.Text.Length;
    }

    private void SetViewModel(SearchViewModel? oldValue, SearchViewModel? newValue)
    {
        if (oldValue is not null)
            oldValue.PropertyChanged -= ViewModel_PropertyChanged;
        DataContext = newValue;
        if (newValue is not null)
            newValue.PropertyChanged += ViewModel_PropertyChanged;
        UpdateChrome();
    }

    private void SetResults(ResultsViewModel? oldValue, ResultsViewModel? newValue)
    {
        if (oldValue is not null)
            oldValue.PropertyChanged -= Results_PropertyChanged;
        if (newValue is not null)
            newValue.PropertyChanged += Results_PropertyChanged;
        UpdateChrome();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(SearchViewModel.IsPageSearch) or nameof(SearchViewModel.IsSearching)
            or nameof(SearchViewModel.SortLabel) or nameof(SearchViewModel.SelectedProvider))
            UpdateChrome();
    }

    private void Results_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ResultsViewModel.IsSearching))
            UpdateChrome();
    }

    private void UpdateChrome()
    {
        if (providerButton is null || ViewModel is null)
            return;

        providerButton.Visibility = providerDivider.Visibility = ViewModel.IsPageSearch ? Visibility.Collapsed : Visibility.Visible;
        busy.Visibility = ViewModel.IsSearching || Results?.IsSearching == true ? Visibility.Visible : Visibility.Collapsed;
        string sortName = $"Sort results. {ViewModel.SortLabel}";
        AutomationProperties.SetName(sortButton, sortName);
        ToolTipService.SetToolTip(sortButton, ViewModel.SortLabel);
    }

    private void Editor_PreviewKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (_textBox is not { SelectionStart: 0, SelectionLength: 0 } || ViewModel.Draft.Terms.Count == 0)
            return;

        if (args.Key is VirtualKey.Left or VirtualKey.Back)
        {
            FocusTerm(ViewModel.Draft.Terms[^1]);
            args.Handled = true;
        }
    }

    private void Term_PreviewKeyDown(object sender, KeyRoutedEventArgs args)
    {
        var term = (SearchTerm)((FrameworkElement)sender).Tag;
        if (args.Key is VirtualKey.Left or VirtualKey.Right)
        {
            SearchTerm? next = ViewModel.Draft.Move(term, args.Key == VirtualKey.Left ? -1 : 1);
            if (next is null)
                FocusTextBoundary();
            else
                FocusTerm(next);
            args.Handled = true;
        }
        else if (args.Key is VirtualKey.Back or VirtualKey.Delete)
        {
            SearchTerm? next = ViewModel.Draft.Move(term, -1) ?? ViewModel.Draft.Move(term, 1);
            ViewModel.Draft.Remove(term);
            DispatcherQueue.TryEnqueue(() =>
            {
                if (next is null || !ViewModel.Draft.Terms.Contains(next))
                    FocusTextBoundary();
                else
                    FocusTerm(next);
            });
            args.Handled = true;
        }
        else if (args.Key == VirtualKey.Escape)
        {
            FocusEditor();
            args.Handled = true;
        }
    }

    private void FocusTerm(SearchTerm term)
    {
        if (termItems.ContainerFromItem(term) is { } container && FindDescendant<Button>(container, "termButton") is { } button)
            button.Focus(FocusState.Keyboard);
    }

    private void FocusTextBoundary()
    {
        editor.Focus(FocusState.Keyboard);
        if (_textBox is not null)
            _textBox.SelectionStart = 0;
    }

    private static T? FindDescendant<T>(DependencyObject parent, string? name = null) where T : FrameworkElement
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match && (name is null || match.Name == name))
                return match;
            if (FindDescendant<T>(child, name) is { } nested)
                return nested;
        }
        return null;
    }
}
