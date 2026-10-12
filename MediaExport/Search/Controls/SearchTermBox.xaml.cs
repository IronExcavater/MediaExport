// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Controls;
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

/// <summary>One term in the search field. At rest it is a button; pressed, its value is edited in place.</summary>
/// <remarks>
/// Enter keeps the value and Escape restores it, both leaving the term selected as a whole. Leaving the term any
/// other way keeps the value too, and a term left empty asks to be removed.
/// </remarks>
public sealed partial class SearchTermBox : UserControl
{
    // How wide the remove button's slot opens, which the Editing state in the XAML animates it to.
    private const double RemoveSlotWidth = 22;

    // Finished when the term has slid out of view.
    private TaskCompletionSource? _slideOut;

    private bool _isEditing;

    // The range box the open picker sets, and whether the picker is only being synced to that box's value.
    private TextBox? _pickedBox;
    private bool _isShowingPickedValue;

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
            valueText.Text = SearchTermText.Describe(value);
            ShowValue();
            ShowParts();

            // The icon stands for the label, so a tooltip says in words what the term does. It is on the resting
            // button, which leaves the boxes without one while the term is edited.
            var explanation = SearchTermText.Explain(value);
            ToolTipService.SetToolTip(restingButton, explanation.Length > 0 ? explanation : null);
            AutomationProperties.SetName(restingButton, value?.ToString());
            AutomationProperties.SetName(valueBox, IsRange ? $"{value?.Label} from" : value?.Label);
            AutomationProperties.SetName(upperBox, $"{value?.Label} to");
        }
    }

    private bool IsRange => Term is not null && SearchQuery.IsRange(Term.Label);

    /// <summary>Raised with the term as it now is, when editing ends with a different value.</summary>
    public event EventHandler<SearchTerm>? Edited;

    /// <summary>Raised when the term should go, by its remove button or by its value being left empty.</summary>
    public event EventHandler? Removed;

    /// <summary>Widens the term from nothing to its full width, for one that has just been added.</summary>
    public void SlideIn()
    {
        // A new box has no term to measure yet, so it stays at no width until its turn on the dispatcher.
        MaxWidth = 0;
        DispatcherQueue.TryEnqueue(() =>
        {
            var unlimited = new Size(double.PositiveInfinity, double.PositiveInfinity);
            content.Measure(unlimited);

            // Leaves room for the remove slot, which opens if the new term starts being edited as it slides in.
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
        // A storyboard holds its last value, so the hold is let go once the term is in and it can grow as it is typed.
        var hasSlidIn = slideWidth.To > 0;
        if (hasSlidIn)
        {
            ClearValue(MaxWidthProperty);
            slide.Stop();
        }

        _slideOut?.TrySetResult();
        _slideOut = null;
    }

    /// <summary>Starts editing with the value selected to type over. A range starts at its first end.</summary>
    public void StartEditing()
    {
        BeginEditing();
        valueBox.Focus(FocusState.Keyboard);
        valueBox.SelectAll();
    }

    /// <summary>Whether the term as a whole has the keyboard, not one of its boxes.</summary>
    public bool IsSelected => restingButton.FocusState != FocusState.Unfocused;

    /// <summary>Gives the keyboard to the term as a whole, which ends any editing. Enter then edits it again.</summary>
    public void Select() => restingButton.Focus(FocusState.Keyboard);

    // A press on a term that is already being edited leaves the editing as it is.
    private void RestingButton_Click(object sender, RoutedEventArgs args)
    {
        if (!_isEditing)
            StartEditing();
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs args) => Removed?.Invoke(this, EventArgs.Empty);

    private void BeginEditing()
    {
        if (_isEditing)
            return;

        _isEditing = true;
        VisualStateManager.GoToState(this, "Editing", useTransitions: false);
        ShowParts();
    }

    private void ValueBox_GotFocus(object sender, RoutedEventArgs args) => OpenPicker((TextBox)sender);

    // A box that already has the keyboard gets no focus event when pressed, so this reopens its picker.
    private void ValueBox_Tapped(object sender, TappedRoutedEventArgs args) => OpenPicker((TextBox)sender);

    private void ValueBox_LostFocus(object sender, RoutedEventArgs args) => DispatcherQueue.TryEnqueue(EndEditingIfLeft);

    private void Picker_Closed(object? sender, object args) => DispatcherQueue.TryEnqueue(EndEditingIfLeft);

    // A range's boxes and pickers are one edit. Where focus went is only known after the event that moved it.
    private void EndEditingIfLeft()
    {
        var focused = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        var isInBoxes = focused == valueBox || focused == upperBox;
        var isInPicker = VisualTree.IsWithin(focused, calendar);
        if (!_isEditing || isInBoxes || isInPicker)
            return;

        durationPicker.Hide();
        datePicker.Hide();
        EndEditing();
    }

    // Opens a range box's picker under it, without taking the keyboard, so the box can still be typed in.
    private void OpenPicker(TextBox box)
    {
        if (!IsRange)
            return;

        var isDuration = Term!.Label == SearchQuery.DurationLabel;
        var picker = isDuration ? durationPicker : datePicker;
        var isAlreadyOpen = picker.IsOpen && _pickedBox == box;
        if (isAlreadyOpen)
            return;

        _pickedBox = box;
        ShowPickedValue();
        var options = new FlyoutShowOptions { ShowMode = FlyoutShowMode.Transient, Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft };
        picker.ShowAt(box, options);
    }

    // Makes the open picker show what is in its box: the hours and minutes, or the day on the calendar.
    private void ShowPickedValue()
    {
        var box = _pickedBox;
        if (box is null || !IsRange)
            return;

        var length = SearchQuery.ReadLength(box.Text) ?? TimeSpan.Zero;
        hoursText.Text = $"{(int)length.TotalHours} h";
        minutesText.Text = $"{length.Minutes} min";

        _isShowingPickedValue = true;
        var date = SearchQuery.ReadDate(box.Text);
        calendar.SelectedDates.Clear();
        if (date is not null)
            calendar.SelectedDates.Add(date.Value);
        calendar.SetDisplayDate(date ?? DateTimeOffset.Now);
        _isShowingPickedValue = false;
    }

    // Typing in the box moves its picker along with it.
    private void ValueBox_TextChanged(object sender, TextChangedEventArgs args)
    {
        if (sender == _pickedBox)
            ShowPickedValue();
    }

    // Steps the duration in the box by the minutes in the pressed button's Tag. It can't go below nothing.
    private void DurationStep_Click(object sender, RoutedEventArgs args)
    {
        var box = _pickedBox;
        if (box is null)
            return;

        var step = int.Parse((string)((FrameworkElement)sender).Tag, CultureInfo.InvariantCulture);
        var length = SearchQuery.ReadLength(box.Text) ?? TimeSpan.Zero;
        var minutes = Math.Max(0, (int)length.TotalMinutes + step);
        box.Text = SearchQuery.NormalizeEnd(SearchQuery.DurationLabel, minutes.ToString(CultureInfo.InvariantCulture));
        box.SelectionStart = box.Text.Length;
    }

    // Picking a day writes it into the box and closes the calendar, with the keyboard back in the box.
    private void Calendar_SelectedDatesChanged(CalendarView sender, CalendarViewSelectedDatesChangedEventArgs args)
    {
        var box = _pickedBox;
        if (_isShowingPickedValue || box is null || args.AddedDates.Count == 0)
            return;

        box.Text = SearchTermText.DescribeEnd(SearchQuery.NormalizeEnd(SearchQuery.PublishedLabel, args.AddedDates[0].ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        box.Focus(FocusState.Programmatic);
        box.SelectionStart = box.Text.Length;
        datePicker.Hide();
    }

    // Leaving the term keeps what was typed, however it was left.
    private void EndEditing()
    {
        _isEditing = false;
        VisualStateManager.GoToState(this, "Resting", useTransitions: false);
        ShowParts();

        var term = Term;
        if (term is null)
            return;

        var value = ReadValue();
        if (value.Length == 0)
            Removed?.Invoke(this, EventArgs.Empty);
        else if (value != term.Value)
            Edited?.Invoke(this, term with { Value = value });
    }

    // What was typed, as the term's value. Range ends are normalised, and a range with neither end is no value.
    private string ReadValue()
    {
        if (!IsRange)
            return valueBox.Text.Trim();

        var label = Term!.Label;
        var lower = SearchQuery.NormalizeEnd(label, valueBox.Text);
        var upper = SearchQuery.NormalizeEnd(label, upperBox.Text);
        var range = new SearchRange(lower, upper);
        return range.IsEmpty ? string.Empty : range.ToString();
    }

    // Puts the term's value into its box, or its two ends into the two boxes of a range.
    private void ShowValue()
    {
        var term = Term;
        // An empty box shows the term's label as its placeholder, or for a range the label of that end.
        if (!IsRange)
        {
            valueBox.Text = term?.Value ?? string.Empty;
            valueBox.PlaceholderText = term?.Label ?? string.Empty;
            return;
        }

        var range = SearchRange.Parse(term!.Value);
        var isDuration = term.Label == SearchQuery.DurationLabel;
        valueBox.Text = SearchTermText.DescribeEnd(range.Lower);
        upperBox.Text = SearchTermText.DescribeEnd(range.Upper);
        valueBox.PlaceholderText = isDuration ? SearchQuery.LongerLabel : SearchQuery.AfterLabel;
        upperBox.PlaceholderText = isDuration ? SearchQuery.ShorterLabel : SearchQuery.BeforeLabel;
    }

    // At rest a term is its value as text. While edited it is a text box, or two with a dash for a range.
    private void ShowParts()
    {
        var showsUpperEnd = _isEditing && IsRange;
        valueText.Visibility = VisibleWhen.True(!_isEditing);
        valueBox.Visibility = VisibleWhen.True(_isEditing);
        rangeDash.Visibility = VisibleWhen.True(showsUpperEnd);
        upperBox.Visibility = VisibleWhen.True(showsUpperEnd);
    }

    // A preview handler, because a TextBox that accepts Return handles Enter and Backspace itself.
    private void ValueBox_PreviewKeyDown(object sender, KeyRoutedEventArgs args)
    {
        var box = (TextBox)sender;
        var isEmpty = box.Text.Length == 0;
        switch (args.Key)
        {
            case VirtualKey.Escape:
                ShowValue();
                Select();
                args.Handled = true;
                break;

            // Backspace in a range's empty second end goes back to its first end.
            case VirtualKey.Back when isEmpty && box == upperBox:
                valueBox.Focus(FocusState.Keyboard);
                valueBox.SelectionStart = valueBox.Text.Length;
                args.Handled = true;
                break;

            // Selecting the term takes focus from its boxes, which keeps the value, or removes a term left empty.
            case VirtualKey.Enter:
            case VirtualKey.Back when isEmpty && upperBox.Text.Length == 0:
                Select();
                args.Handled = true;
                break;
        }
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
