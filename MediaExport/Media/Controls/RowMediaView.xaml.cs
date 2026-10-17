// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Xaml;

namespace MediaExport.Media.Controls;

/// <summary>Shows one media result as a row, for lists such as the search field's results.</summary>
public sealed partial class RowMediaView : MediaView
{
    public static readonly DependencyProperty PresentationProperty = DependencyProperty.Register(
        nameof(Presentation),
        typeof(MediaPresentation),
        typeof(RowMediaView),
        new PropertyMetadata(MediaPresentation.Comfortable, OnPresentationChanged));

    public static readonly DependencyProperty ThumbnailHeightProperty = DependencyProperty.Register(
        nameof(ThumbnailHeight), typeof(double), typeof(RowMediaView), new PropertyMetadata(112d));

    public static readonly DependencyProperty ContentPaddingProperty = DependencyProperty.Register(
        nameof(ContentPadding), typeof(Thickness), typeof(RowMediaView), new PropertyMetadata(new Thickness(0, 8, 0, 8)));

    public static readonly DependencyProperty ContentSpacingProperty = DependencyProperty.Register(
        nameof(ContentSpacing), typeof(double), typeof(RowMediaView), new PropertyMetadata(16d));

    public static readonly DependencyProperty TitleFontSizeProperty = DependencyProperty.Register(
        nameof(TitleFontSize), typeof(double), typeof(RowMediaView), new PropertyMetadata(16d));

    public static readonly DependencyProperty DetailsProperty = DependencyProperty.Register(
        nameof(Details), typeof(object), typeof(RowMediaView), new PropertyMetadata(null));

    public RowMediaView()
    {
        InitializeComponent();
    }

    /// <summary>A comfortable row by default, or a compact row for constrained surfaces such as search suggestions.</summary>
    public MediaPresentation Presentation
    {
        get => (MediaPresentation)GetValue(PresentationProperty);
        set => SetValue(PresentationProperty, value);
    }

    /// <summary>The thumbnail height. Consumers may override the presentation preset.</summary>
    public double ThumbnailHeight
    {
        get => (double)GetValue(ThumbnailHeightProperty);
        set => SetValue(ThumbnailHeightProperty, value);
    }

    /// <summary>The padding around the row. Consumers may override the presentation preset.</summary>
    public Thickness ContentPadding
    {
        get => (Thickness)GetValue(ContentPaddingProperty);
        set => SetValue(ContentPaddingProperty, value);
    }

    /// <summary>The gap between the thumbnail and text. Consumers may override the presentation preset.</summary>
    public double ContentSpacing
    {
        get => (double)GetValue(ContentSpacingProperty);
        set => SetValue(ContentSpacingProperty, value);
    }

    /// <summary>The title size. Consumers may override the presentation preset.</summary>
    public double TitleFontSize
    {
        get => (double)GetValue(TitleFontSizeProperty);
        set => SetValue(TitleFontSizeProperty, value);
    }

    /// <summary>Optional content placed below the shared title and byline without duplicating the media layout.</summary>
    public object? Details
    {
        get => GetValue(DetailsProperty);
        set => SetValue(DetailsProperty, value);
    }

    private static void OnPresentationChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var view = (RowMediaView)sender;
        var compact = (MediaPresentation)args.NewValue == MediaPresentation.Compact;
        view.ThumbnailHeight = compact ? 56 : 112;
        view.ContentPadding = compact ? new Thickness(0, 6, 0, 6) : new Thickness(0, 8, 0, 8);
        view.ContentSpacing = compact ? 12 : 16;
        view.TitleFontSize = compact ? 14 : 16;
    }
}
