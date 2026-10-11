// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using MediaExport.Core.Media.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Linq;

namespace MediaExport.Media.Controls;

/// <summary>
/// The base of every control that shows one media result. A variant is a XAML file whose root is this class
/// and which arranges the same parts in its own way:
/// <list type="bullet">
/// <item><see cref="MediaThumbnail"/>: the picture, with the duration over it.</item>
/// <item><see cref="MediaByline"/>: the line saying where it is from, who made it, its views and its age.</item>
/// <item><see cref="RowMediaView"/>: a row, for lists.</item>
/// <item><see cref="CardMediaView"/>: a card, for grids.</item>
/// </list>
/// The variants share their words too: the <b>thumbnail</b> is the picture, the <b>title</b> is its name, and
/// the <b>byline</b> is the line under it saying who made it, its views and its age, after its provider's icon.
/// </summary>
public abstract partial class MediaView : UserControl
{
    public static readonly DependencyProperty MediaProperty = DependencyProperty.Register(
        nameof(Media),
        typeof(MediaSearchResult),
        typeof(MediaView),
        new PropertyMetadata(null, OnMediaPropertyChanged));

    /// <summary>The media to show. A variant reads its parts with <c>{x:Bind Media.Title, Mode=OneWay}</c>.</summary>
    public MediaSearchResult? Media
    {
        get => (MediaSearchResult?)GetValue(MediaProperty);
        set => SetValue(MediaProperty, value);
    }

    /// <summary>
    /// The icon of the provider with this id, as a resource key for an <c>IconView</c>. A variant shows where
    /// its media came from with <c>{x:Bind ProviderIcon(Media.ProviderId), Mode=OneWay}</c>.
    /// </summary>
    protected string? ProviderIcon(string? providerId)
    {
        var providers = App.Current.Services.GetServices<IMediaSearchProvider>();
        var provider = providers.FirstOrDefault(candidate => candidate.Id == providerId);
        return provider?.Icon;
    }

    /// <summary>
    /// Called when <see cref="Media"/> changes, for a variant with something to do in code. It is given both
    /// values so it can redo only what is different: a list reuses its rows, so a view is handed one media
    /// after another, often with parts in common.
    /// </summary>
    protected virtual void OnMediaChanged(MediaSearchResult? oldMedia, MediaSearchResult? newMedia)
    {
    }

    private static void OnMediaPropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var view = (MediaView)sender;
        view.OnMediaChanged((MediaSearchResult?)args.OldValue, (MediaSearchResult?)args.NewValue);
    }
}
