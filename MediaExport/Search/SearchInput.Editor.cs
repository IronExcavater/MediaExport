// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using Windows.ApplicationModel.DataTransfer;

namespace MediaExport.Search;

/// <summary>Typing, clipboard and submit behavior for the unified search field.</summary>
public sealed partial class SearchInput
{
    private void Editor_Loaded(object sender, RoutedEventArgs args)
    {
        _textBox = FindDescendant<TextBox>(editor);
        if (_textBox is null)
            return;

        _textBox.Paste += async (_, paste) =>
        {
            bool replacesWholeField = ViewModel.Draft.Terms.Count > 0
                && (_textBox.Text.Length == 0 || _textBox is { SelectionStart: 0 } && _textBox.SelectionLength == _textBox.Text.Length);
            if (!replacesWholeField)
            {
                DispatcherQueue.TryEnqueue(() => ResolveTerms(includeUnfinished: true));
                return;
            }

            DataPackageView content = Clipboard.GetContent();
            if (!content.Contains(StandardDataFormats.Text))
                return;

            paste.Handled = true;
            try
            {
                ViewModel.Replace(await content.GetTextAsync());
                editor.Text = ViewModel.SearchText;
                _textBox.SelectionStart = _textBox.Text.Length;
            }
            catch (Exception)
            {
                // The clipboard can be replaced or become unavailable between the Paste event and the read.
            }
        };
        _textBox.CopyingToClipboard += (_, copy) => copy.Handled = CopyWholeSearch();
        _textBox.CuttingToClipboard += (_, cut) =>
        {
            if (cut.Handled = CopyWholeSearch())
            {
                ViewModel.Draft.Cut();
                editor.Text = string.Empty;
            }
        };
    }

    private bool CopyWholeSearch()
    {
        if (_textBox is null || (_textBox.SelectionLength > 0 && _textBox.SelectionLength < _textBox.Text.Length))
            return false;

        var package = new DataPackage();
        package.SetText(ViewModel.FullText);
        Clipboard.SetContent(package);
        return true;
    }

    private bool ResolveTerms(bool includeUnfinished)
    {
        bool changed = ViewModel.ResolveTerms(includeUnfinished);
        if (changed)
        {
            editor.Text = ViewModel.SearchText;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_textBox is not null)
                    _textBox.SelectionStart = _textBox.Text.Length;
            });
        }
        return changed;
    }

    private void Editor_GotFocus(object sender, RoutedEventArgs args)
    {
        if (!ViewModel.IsPageSearch)
            ViewModel.ShowSuggestions();
    }

    private void Editor_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason is not AutoSuggestionBoxTextChangeReason.UserInput)
            return;

        ViewModel.SearchText = sender.Text;
        if (ResolveTerms(includeUnfinished: false))
            return;

        if (!ViewModel.IsPageSearch || SearchViewModel.IsLink(ViewModel.SearchText.Trim()))
            _ = ViewModel.SearchAsync();
    }

    private void Editor_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        switch (args.ChosenSuggestion)
        {
            case SearchSuggestion { Media: { } media }:
                MediaChosen?.Invoke(this, media);
                return;
            case SearchSuggestion { IsRecentSearch: true } recent:
                ViewModel.Restore(recent.Query);
                editor.Text = ViewModel.SearchText;
                Submitted?.Invoke(this, EventArgs.Empty);
                return;
        }

        ViewModel.SearchText = sender.Text;
        ResolveTerms(includeUnfinished: true);
        Submitted?.Invoke(this, EventArgs.Empty);
    }

    private void Search_Click(object sender, RoutedEventArgs args)
    {
        ViewModel.SearchText = editor.Text;
        ResolveTerms(includeUnfinished: true);
        Submitted?.Invoke(this, EventArgs.Empty);
    }
}
