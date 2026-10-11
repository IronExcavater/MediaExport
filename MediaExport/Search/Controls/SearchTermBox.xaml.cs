// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Providers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Threading.Tasks;
using Windows.Foundation;
using VirtualKey = Windows.System.VirtualKey;

namespace MediaExport.Search.Controls;

/// <summary>
/// One search term in the search field, with its value in a text box so it is edited where it stands. Enter
/// or leaving the box keeps the new value, Escape puts the old one back, and a term left with no value asks
/// to be removed. A term can't be changed, so the control reports the edited one and its host replaces it.
/// </summary>
public sealed partial class SearchTermBox : UserControl
{
    // How wide the remove button's slot opens, which the Editing state in the XAML animates it to.
    private const double RemoveSlotWidth = 22;

    // Finished when the term has slid out of view.
    private TaskCompletionSource? _slideOut;

    public SearchTermBox()
    {
        InitializeComponent();
        Storyboard.SetTarget(slideWidth, this);
    }

    public SearchTerm? Term
    {
        get;
        set
        {
            field = value;
            labelIcon.Glyph = GlyphFor(value?.Label);
            valueBox.Text = value?.Value ?? string.Empty;

            // The icon stands for the label, so the label is given in words to a tooltip and to screen readers.
            ToolTipService.SetToolTip(this, value?.Label);
            AutomationProperties.SetName(valueBox, value?.Label);
        }
    }

    // The same icons as the menu the terms are added from, in the Segoe Fluent Icons font.
    private static string GlyphFor(string? label) => label switch
    {
        SearchQuery.ExactLabel => "\uE718",
        SearchQuery.ExcludeLabel => "\uE733",
        SearchQuery.AuthorLabel => "\uE77B",
        _ => string.Empty
    };

    /// <summary>Raised with the term as it now is, when editing ends with a different value.</summary>
    public event EventHandler<SearchTerm>? Edited;

    /// <summary>Raised when the term should go: its remove button, or its value left empty.</summary>
    public event EventHandler? Removed;

    /// <summary>Raised when a key ends the editing, for the host to put the keyboard back in its own box.</summary>
    public event EventHandler? EditingEnded;

    /// <summary>Widens the term from nothing to its full width, for one that has just been added.</summary>
    public void SlideIn()
    {
        // A box that has just been made doesn't have its term yet, so its full width can't be measured. It
        // is kept at no width until its turn on the dispatcher, by when the term is in place.
        MaxWidth = 0;
        DispatcherQueue.TryEnqueue(() =>
        {
            var unlimited = new Size(double.PositiveInfinity, double.PositiveInfinity);
            content.Measure(unlimited);

            // A term added from the menu starts being edited as it slides in, which opens its remove
            // button's slot. The slide makes room for the slot whether or not it opens, or the button would
            // be cut off until the slide had finished.
            slideWidth.From = 0;
            slideWidth.To = content.DesiredSize.Width + RemoveSlotWidth;
            slide.Begin();
        });
    }

    /// <summary>Narrows the term to nothing, for one that is about to be removed.</summary>
    /// <returns>A task that finishes when the term is out of view.</returns>
    public Task SlideOutAsync()
    {
        _slideOut = new TaskCompletionSource();
        slideWidth.From = ActualWidth;
        slideWidth.To = 0;
        slide.Begin();
        return _slideOut.Task;
    }

    private void Slide_Completed(object? sender, object args)
    {
        // A storyboard holds its last value. Once the term is fully in, the hold is let go, so the term can
        // grow as its value is typed. A term that slid out stays at no width until it is removed.
        var hasSlidIn = slideWidth.To > 0;
        if (hasSlidIn)
        {
            ClearValue(MaxWidthProperty);
            slide.Stop();
        }

        _slideOut?.TrySetResult();
        _slideOut = null;
    }

    /// <summary>Puts the keyboard in the value, all of it selected to type over.</summary>
    public void StartEditing()
    {
        valueBox.Focus(FocusState.Keyboard);
        valueBox.SelectAll();
    }

    /// <summary>Puts the keyboard at the end of the value, as when it is reached from the box after it.</summary>
    public void StartEditingAtEnd()
    {
        valueBox.Focus(FocusState.Keyboard);
        valueBox.SelectionStart = valueBox.Text.Length;
    }

    private void RestingButton_Click(object sender, RoutedEventArgs args) => StartEditing();

    private void RemoveButton_Click(object sender, RoutedEventArgs args) => Removed?.Invoke(this, EventArgs.Empty);

    private void ValueBox_GotFocus(object sender, RoutedEventArgs args) => VisualStateManager.GoToState(this, "Editing", useTransitions: false);

    // Leaving the box keeps what was typed, however it was left.
    private void ValueBox_LostFocus(object sender, RoutedEventArgs args)
    {
        VisualStateManager.GoToState(this, "Resting", useTransitions: false);

        var term = Term;
        if (term is null)
            return;

        var value = valueBox.Text.Trim();
        if (value.Length == 0)
            Removed?.Invoke(this, EventArgs.Empty);
        else if (value != term.Value)
            Edited?.Invoke(this, term with { Value = value });
    }

    // A preview handler, because a TextBox that accepts Return handles Enter and Backspace itself.
    private void ValueBox_PreviewKeyDown(object sender, KeyRoutedEventArgs args)
    {
        var isEmpty = valueBox.Text.Length == 0;
        switch (args.Key)
        {
            case VirtualKey.Escape:
                valueBox.Text = Term?.Value ?? string.Empty;
                EditingEnded?.Invoke(this, EventArgs.Empty);
                args.Handled = true;
                break;

            // Ending the editing takes focus away, which is what keeps the value, or removes a term left empty.
            case VirtualKey.Enter:
            case VirtualKey.Back when isEmpty:
                EditingEnded?.Invoke(this, EventArgs.Empty);
                args.Handled = true;
                break;
        }
    }
}
