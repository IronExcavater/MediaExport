// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Controls;
using MediaExport.Core.Media.Providers;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
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

    public SearchInput()
    {
        InitializeComponent();
    }

    public SearchViewModel? ViewModel
    {
        get => (SearchViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    /// <summary>Raised when the Search button is pressed, to show the results as a page.</summary>
    public event EventHandler? Submitted;

    /// <summary>Moves keyboard focus to the text box.</summary>
    public void FocusEditor() => editor.Focus(FocusState.Keyboard);

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

    // A horizontal ScrollViewer gives its content only the width it asks for, so the strip is made at least
    // as wide as the field. The text box's star column then takes whatever room is spare.
    private void FieldScroll_SizeChanged(object sender, SizeChangedEventArgs args) => strip.MinWidth = args.NewSize.Width;

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

    // Enter runs the search and lists the results under the box.
    private void Editor_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) =>
        ViewModel?.SearchCommand.Execute(null);

    private async void QueryActionsMenu_Opening(object? sender, object args)
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
        catch (Exception)
        {
            // Another app can have the clipboard locked. Paste stays disabled until the menu is opened again.
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
            FocusEditor();
        }
        catch (Exception)
        {
            // The clipboard can change or be locked by another app between opening the menu and pasting.
        }
    }

    private void ClearQuery_Click(object sender, RoutedEventArgs args)
    {
        ViewModel?.Clear();
        FocusEditor();
    }

    private void Search_Click(object sender, RoutedEventArgs args) => Submitted?.Invoke(this, EventArgs.Empty);
}
