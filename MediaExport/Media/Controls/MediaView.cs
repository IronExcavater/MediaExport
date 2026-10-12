// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using MediaExport.Core.Media.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Linq;

namespace MediaExport.Media.Controls;

/// <summary>The base of every control that shows one media result. Without media it shows its skeleton.</summary>
public abstract partial class MediaView : UserControl
{
    public static readonly DependencyProperty MediaProperty = DependencyProperty.Register(
        nameof(Media),
        typeof(MediaSearchResult),
        typeof(MediaView),
        new PropertyMetadata(null, OnMediaPropertyChanged));

    // Fades a skeleton in and out slowly, the sign that something is loading.
    private readonly Storyboard _skeletonPulse = new();

    protected MediaView()
    {
        var fade = new DoubleAnimation
        {
            From = 1,
            To = 0.45,
            Duration = TimeSpan.FromSeconds(0.9),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(fade, this);
        Storyboard.SetTargetProperty(fade, nameof(Opacity));
        _skeletonPulse.Children.Add(fade);

        // A view that is never given media gets no change to react to, so its first look is set once it loads.
        Loaded += (_, _) => UpdateSkeleton();
        Unloaded += (_, _) => _skeletonPulse.Stop();
    }

    /// <summary>The media to show. A variant reads its parts with <c>{x:Bind Media.Title, Mode=OneWay}</c>.</summary>
    public MediaSearchResult? Media
    {
        get => (MediaSearchResult?)GetValue(MediaProperty);
        set => SetValue(MediaProperty, value);
    }

    /// <summary>The resource key of a provider's icon, for <c>{x:Bind ProviderIcon(Media.ProviderId)}</c>.</summary>
    protected string? ProviderIcon(string? providerId)
    {
        var providers = App.Current.Services.GetServices<IMediaSearchProvider>();
        var provider = providers.FirstOrDefault(candidate => candidate.Id == providerId);
        return provider?.Icon;
    }

    /// <summary>Called when <see cref="Media"/> changes. A reused view can redo only what differs.</summary>
    protected virtual void OnMediaChanged(MediaSearchResult? oldMedia, MediaSearchResult? newMedia)
    {
    }

    // With no media a variant goes to its Skeleton visual state; one without it, such as the thumbnail, is plain.
    private void UpdateSkeleton()
    {
        var isLoading = Media is null;
        var state = isLoading ? "Skeleton" : "Loaded";
        var hasSkeleton = VisualStateManager.GoToState(this, state, useTransitions: false);
        if (isLoading && hasSkeleton && IsLoaded)
            _skeletonPulse.Begin();
        else
            _skeletonPulse.Stop();
    }

    private static void OnMediaPropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var view = (MediaView)sender;
        view.UpdateSkeleton();
        view.OnMediaChanged((MediaSearchResult?)args.OldValue, (MediaSearchResult?)args.NewValue);
    }
}
