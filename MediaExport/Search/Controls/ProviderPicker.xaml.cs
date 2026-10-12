// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Providers;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Specialized;
using System.Linq;
using Windows.UI.Core;
using VirtualKey = Windows.System.VirtualKey;

namespace MediaExport.Search.Controls;

/// <summary>
/// Lists the search providers and chooses which are searched. A press chooses one provider alone. With the toggle
/// on, which it is while Ctrl is held, a press adds or removes one.
/// </summary>
public sealed partial class ProviderPicker : UserControl
{
    // Set while the rows are being selected to match the chosen providers.
    private bool _isShowingProviders;

    public ProviderPicker()
    {
        InitializeComponent();
    }

    public SearchViewModel? ViewModel
    {
        get;
        set
        {
            if (field is not null)
                field.SelectedProviders.CollectionChanged -= SelectedProviders_CollectionChanged;

            field = value;
            providerList.ItemsSource = value?.Providers;
            if (value is not null)
                value.SelectedProviders.CollectionChanged += SelectedProviders_CollectionChanged;
        }
    }

    /// <summary>Raised when one provider has been chosen alone, which is when a flyout holding the picker closes.</summary>
    public event EventHandler? OneChosen;

    /// <summary>Makes the picker show what is chosen, for when it is about to be shown.</summary>
    public void Refresh()
    {
        IsChoosingSeveral = IsCtrlHeld;
        ShowChosenProviders();
    }

    /// <summary>
    /// Puts the focus on the chosen row as if by the pointer, so that no focus ring shows until a key is pressed.
    /// A flyout otherwise focuses its first control as if by the keyboard when the keyboard was last used.
    /// </summary>
    public void FocusChosenRow()
    {
        var chosen = ViewModel?.SelectedProviders.FirstOrDefault();
        var row = chosen is null ? null : providerList.ContainerFromItem(chosen) as Control;
        row?.Focus(FocusState.Pointer);
    }

    // Whether a press adds or removes a provider instead of choosing it alone.
    private bool IsChoosingSeveral
    {
        get => severalButton.IsChecked == true;
        set => severalButton.IsChecked = value;
    }

    private static bool IsCtrlHeld
    {
        get
        {
            var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
            return ctrl.HasFlag(CoreVirtualKeyStates.Down);
        }
    }

    // The toggle is on for as long as Ctrl is held, whatever is chosen.
    private void Root_CtrlChanged(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Control)
            IsChoosingSeveral = IsCtrlHeld;
    }

    private void SelectedProviders_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs args) =>
        ShowChosenProviders();

    // Selects the chosen providers' rows, and says what the button beside the toggle now does.
    private void ShowChosenProviders()
    {
        var viewModel = ViewModel;
        if (viewModel is null)
            return;

        var chosen = viewModel.SelectedProviders.ToList();
        var selected = providerList.SelectedItems.Cast<IMediaSearchProvider>().ToList();
        var isShown = chosen.Count == selected.Count && chosen.All(selected.Contains);
        if (!isShown)
        {
            _isShowingProviders = true;
            providerList.SelectedItems.Clear();
            foreach (var provider in chosen)
                providerList.SelectedItems.Add(provider);
            _isShowingProviders = false;
        }

        var isAll = chosen.Count == viewModel.Providers.Count;
        allProvidersIcon.Glyph = isAll ? "\uF16C" : "\uF16B";
        var action = isAll ? "Deselect all" : "Select all";
        allProvidersText.Text = action;
        AutomationProperties.SetName(allProvidersButton, action);
    }

    // The list only shows what is chosen. Its own idea of what a press or an arrow key selects is put back, so the
    // rows look the same whether one or several are being chosen, and ProviderList_ItemClick decides what a press does.
    private void ProviderList_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!_isShowingProviders)
            ShowChosenProviders();
    }

    private void ProviderList_ItemClick(object sender, ItemClickEventArgs args)
    {
        var viewModel = ViewModel;
        if (viewModel is null)
            return;

        var provider = (IMediaSearchProvider)args.ClickedItem;
        if (!IsChoosingSeveral && !IsCtrlHeld)
        {
            viewModel.SelectProviders([provider]);
            OneChosen?.Invoke(this, EventArgs.Empty);
            return;
        }

        // A search needs a provider, so removing the last one is ignored.
        var chosen = viewModel.SelectedProviders.ToList();
        var wasChosen = chosen.Remove(provider);
        if (!wasChosen)
            chosen.Add(provider);

        viewModel.SelectProviders(chosen);
    }

    // Deselecting leaves the first provider, since a search needs one. Selecting them all turns the toggle on, as
    // the next press is most likely to take one away again.
    private void AllProvidersButton_Click(object sender, RoutedEventArgs args)
    {
        var viewModel = ViewModel;
        if (viewModel is null)
            return;

        var isAll = viewModel.SelectedProviders.Count == viewModel.Providers.Count;
        IsChoosingSeveral = !isAll;
        viewModel.SelectProviders(isAll ? viewModel.Providers.Take(1) : viewModel.Providers);
    }
}
