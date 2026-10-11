// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Converters;
using MediaExport.Core.Media.Providers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Globalization;
using System.Threading.Tasks;
using Windows.Foundation;
using VirtualKey = Windows.System.VirtualKey;

namespace MediaExport.Search.Controls;

/// <summary>
/// One search term in the search field. At rest it is a button showing its kind as an icon and its value as
/// text. Pressing it edits the value: a term of words turns into a text box where it stands, and a range, of
/// durations or dates, opens a flyout with a box or date picker for each end.
/// <para>
/// Enter or leaving keeps what was set, Escape puts the old value back, and a term left with no value asks to
/// be removed. A term can't be changed, so the control reports the edited one and its host replaces it.
/// </para>
/// </summary>
public sealed partial class SearchTermBox : UserControl
{
    // How a date is written in a range's value.
    private const string DateFormat = "yyyy-MM-dd";

    // How wide the remove button's slot opens, which the Editing state in the XAML animates it to.
    private const double RemoveSlotWidth = 22;

    // The earliest and latest days a date picker offers when the other end doesn't limit it.
    private readonly DateTimeOffset _earliestDate;
    private readonly DateTimeOffset _latestDate;

    // Finished when the term has slid out of view.
    private TaskCompletionSource? _slideOut;

    // Whether Escape was what closed the range flyout, which gives up the changes made in it.
    private bool _isRangeEditCancelled;

    public SearchTermBox()
    {
        InitializeComponent();
        Storyboard.SetTarget(slideWidth, this);
        _earliestDate = afterPicker.MinDate;
        _latestDate = afterPicker.MaxDate;
    }

    public SearchTerm? Term
    {
        get;
        set
        {
            field = value;
            labelIcon.Glyph = GlyphFor(value?.Label);
            valueText.Text = Describe(value);
            valueBox.Text = value?.Value ?? string.Empty;

            // The icon stands for the label, so the label is given in words to a tooltip and to screen readers.
            ToolTipService.SetToolTip(this, value?.Label);
            AutomationProperties.SetName(restingButton, value?.ToString());
            AutomationProperties.SetName(valueBox, value?.Label);
        }
    }

    private bool IsRange => Term is not null && SearchQuery.IsRange(Term.Label);

    private bool IsDuration => Term?.Label == SearchQuery.DurationLabel;

    /// <summary>Raised with the term as it now is, when editing ends with a different value.</summary>
    public event EventHandler<SearchTerm>? Edited;

    /// <summary>Raised when the term should go: its remove button, or its value left empty.</summary>
    public event EventHandler? Removed;

    /// <summary>Raised when the editing is over, for the host to put the keyboard back in its own box.</summary>
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

            // A term of words added from the menu starts being edited as it slides in, which opens its
            // remove button's slot. The slide makes room for the slot whether or not it opens, or the
            // button would be cut off until the slide had finished.
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

    /// <summary>Starts editing the value: all of it selected to type over, or a range's flyout open.</summary>
    public void StartEditing()
    {
        if (IsRange)
        {
            OpenRangeFlyout();
            return;
        }

        VisualStateManager.GoToState(this, "Editing", useTransitions: false);
        valueBox.Focus(FocusState.Keyboard);
        valueBox.SelectAll();
    }

    /// <summary>Starts editing with the cursor at the end of the value, as when it is reached from the box after it.</summary>
    public void StartEditingAtEnd()
    {
        StartEditing();
        valueBox.SelectionStart = valueBox.Text.Length;
    }

    private void RestingButton_Click(object sender, RoutedEventArgs args) => StartEditing();

    private void RemoveButton_Click(object sender, RoutedEventArgs args)
    {
        if (!IsRange)
        {
            Removed?.Invoke(this, EventArgs.Empty);
            return;
        }

        // The flyout's Remove button empties the range, and closing the flyout with an empty range removes it.
        ShowRange(new SearchRange(string.Empty, string.Empty));
        rangeFlyout.Hide();
    }

    // Leaving the box keeps what was typed, however it was left.
    private void ValueBox_LostFocus(object sender, RoutedEventArgs args)
    {
        VisualStateManager.GoToState(this, "Resting", useTransitions: false);
        Commit(valueBox.Text.Trim());
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

    // Reports what the term was left as: gone if it has no value, edited if the value is different.
    private void Commit(string value)
    {
        var term = Term;
        if (term is null)
            return;

        if (value.Length == 0)
            Removed?.Invoke(this, EventArgs.Empty);
        else if (value != term.Value)
            Edited?.Invoke(this, term with { Value = value });
    }

    private void OpenRangeFlyout()
    {
        _isRangeEditCancelled = false;
        durationEditor.Visibility = VisibleWhen.True(IsDuration);
        publishedEditor.Visibility = VisibleWhen.True(!IsDuration);
        ShowRange(SearchRange.Parse(Term!.Value));
        FlyoutBase.ShowAttachedFlyout(restingButton);
    }

    // Puts a range into the flyout's boxes or date pickers.
    private void ShowRange(SearchRange range)
    {
        if (IsDuration)
        {
            longerBox.Text = range.Lower;
            shorterBox.Text = range.Upper;
        }
        else
        {
            afterPicker.Date = SearchQuery.ReadDate(range.Lower);
            beforePicker.Date = SearchQuery.ReadDate(range.Upper);
        }
    }

    // Closing the flyout keeps the range it holds. Escape first puts the term's own range back into it, so
    // nothing changes, and a term that never had a range is then empty and is removed.
    private void RangeFlyout_Closed(object? sender, object args)
    {
        if (_isRangeEditCancelled)
            ShowRange(SearchRange.Parse(Term!.Value));

        Commit(ReadRange());
        EditingEnded?.Invoke(this, EventArgs.Empty);
    }

    // What the flyout was left holding, as the term's value. A range with neither end is no value at all.
    private string ReadRange()
    {
        var range = IsDuration
            ? new SearchRange(longerBox.Text.Trim(), shorterBox.Text.Trim())
            : new SearchRange(Write(afterPicker.Date), Write(beforePicker.Date));

        return range.IsEmpty ? string.Empty : range.ToString();
    }

    // Enter closes the flyout, which keeps what was set. Escape closes it too, by itself; this only notes that
    // the changes are to be given up. A preview handler sees Escape before the flyout acts on it.
    private void RangeEditor_PreviewKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Escape)
        {
            _isRangeEditCancelled = true;
        }
        else if (args.Key == VirtualKey.Enter)
        {
            rangeFlyout.Hide();
            args.Handled = true;
        }
    }

    // The Tag of a ready-made duration is the range, as it is written in a term's value.
    private void DurationPreset_Click(object sender, RoutedEventArgs args)
    {
        var button = (Button)sender;
        ShowRange(SearchRange.Parse((string)button.Tag));
        rangeFlyout.Hide();
    }

    // The Tag of a ready-made date range is how many days back it starts. It has no end.
    private void PublishedPreset_Click(object sender, RoutedEventArgs args)
    {
        var button = (Button)sender;
        var days = int.Parse((string)button.Tag, CultureInfo.InvariantCulture);
        afterPicker.Date = DateTimeOffset.Now.Date.AddDays(-days);
        beforePicker.Date = null;
        rangeFlyout.Hide();
    }

    // Each picker only offers days that leave something between the two ends.
    private void PublishedPicker_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        var dayAfterStart = afterPicker.Date?.AddDays(1);
        var dayBeforeEnd = beforePicker.Date?.AddDays(-1);
        beforePicker.MinDate = dayAfterStart ?? _earliestDate;
        afterPicker.MaxDate = dayBeforeEnd ?? _latestDate;
    }

    private static string Write(DateTimeOffset? date) =>
        date is null ? string.Empty : date.Value.ToString(DateFormat, CultureInfo.InvariantCulture);

    // The value as it reads at rest. A range reads as one piece of text: both ends with a dash between
    // them, or its one end after a sign for which end it is.
    private static string Describe(SearchTerm? term)
    {
        if (term is null || !SearchQuery.IsRange(term.Label))
            return term?.Value ?? string.Empty;

        var range = SearchRange.Parse(term.Value);
        var isDuration = term.Label == SearchQuery.DurationLabel;
        var lower = isDuration ? range.Lower : DescribeDate(range.Lower);
        var upper = isDuration ? range.Upper : DescribeDate(range.Upper);
        if (lower.Length > 0 && upper.Length > 0)
            return $"{lower} \u2013 {upper}";

        return lower.Length > 0 ? $"> {lower}" : $"< {upper}";
    }

    // A date reads as day, month and year however it was written: a year alone is its first of January.
    private static string DescribeDate(string written)
    {
        var date = SearchQuery.ReadDate(written);
        return date is null ? written : date.Value.ToString("d MMMM yyyy", CultureInfo.CurrentCulture);
    }

    // The same icons as the menu the terms are added from, in the Segoe Fluent Icons font.
    private static string GlyphFor(string? label) => label switch
    {
        SearchQuery.ExactLabel => "\uE718",
        SearchQuery.ExcludeLabel => "\uE733",
        SearchQuery.AuthorLabel => "\uE77B",
        SearchQuery.DurationLabel => "\uE916",
        SearchQuery.PublishedLabel => "\uE787",
        _ => string.Empty
    };
}
