// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using System;

namespace MediaExport.Controls;

/// <summary>
/// The transition WinUI has no property for: one that runs when an element's <c>Visibility</c> changes. It is
/// set on an element beside the built-in ones such as <c>OpacityTransition</c>:
/// <code>
/// &lt;Button controls:VisibilityTransition.Fade="True" /&gt;
/// </code>
/// </summary>
/// <remarks>
/// Collapsing an element hides it at once, so an <c>OpacityTransition</c> can't fade it out. This uses the
/// composition layer's show and hide animations, which run on the picture of the element and keep it on
/// screen until the animation ends.
/// </remarks>
public static class VisibilityTransition
{
    /// <summary>How long a fade lasts. An <c>OpacityTransition</c> beside one of these should last as long.</summary>
    public static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(200);

    /// <summary>Whether the element fades in when it is shown and fades out when it is collapsed.</summary>
    public static readonly DependencyProperty FadeProperty = DependencyProperty.RegisterAttached(
        "Fade",
        typeof(bool),
        typeof(VisibilityTransition),
        new PropertyMetadata(false, OnFadeChanged));

    public static bool GetFade(UIElement element) => (bool)element.GetValue(FadeProperty);

    public static void SetFade(UIElement element, bool value) => element.SetValue(FadeProperty, value);

    private static void OnFadeChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var element = (UIElement)sender;
        var isFading = (bool)args.NewValue;
        var compositor = ElementCompositionPreview.GetElementVisual(element).Compositor;

        var fadeIn = isFading ? CreateFade(compositor, from: 0, to: 1) : null;
        var fadeOut = isFading ? CreateFade(compositor, from: 1, to: 0) : null;
        ElementCompositionPreview.SetImplicitShowAnimation(element, fadeIn);
        ElementCompositionPreview.SetImplicitHideAnimation(element, fadeOut);
    }

    private static ScalarKeyFrameAnimation CreateFade(Compositor compositor, float from, float to)
    {
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.Target = nameof(Visual.Opacity);
        fade.Duration = Duration;
        fade.InsertKeyFrame(0f, from);
        fade.InsertKeyFrame(1f, to);
        return fade;
    }
}
