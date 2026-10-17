// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using System;

namespace MediaExport.Controls;

/// <summary>Fades an element when its <c>Visibility</c> changes, which WinUI has no transition for.</summary>
/// <remarks>Uses composition show and hide animations, which keep a collapsed element drawn until they end.</remarks>
public static class VisibilityTransition
{
    /// <summary>How long a fade lasts. An <c>OpacityTransition</c> beside one of these should last as long.</summary>
    public static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(200);

    // How far an element that slides moves, in from the left and back out to it.
    private const float SlideDistance = 12;

    /// <summary>Whether the element fades in when it is shown and fades out when it is collapsed.</summary>
    public static readonly DependencyProperty FadeProperty = DependencyProperty.RegisterAttached(
        "Fade",
        typeof(bool),
        typeof(VisibilityTransition),
        new PropertyMetadata(false, OnChanged));

    /// <summary>Whether the element fades as with <see cref="FadeProperty"/> and slides a little way sideways too.</summary>
    public static readonly DependencyProperty SlideProperty = DependencyProperty.RegisterAttached(
        "Slide",
        typeof(bool),
        typeof(VisibilityTransition),
        new PropertyMetadata(false, OnChanged));

    /// <summary>
    /// Whether the element fades and slides when shown, but leaves the layout immediately when collapsed.
    /// Useful when its parent is shrinking and retaining the hidden visual would make its content reflow.
    /// </summary>
    public static readonly DependencyProperty ShowSlideProperty = DependencyProperty.RegisterAttached(
        "ShowSlide",
        typeof(bool),
        typeof(VisibilityTransition),
        new PropertyMetadata(false, OnChanged));

    public static bool GetFade(UIElement element) => (bool)element.GetValue(FadeProperty);

    public static void SetFade(UIElement element, bool value) => element.SetValue(FadeProperty, value);

    public static bool GetSlide(UIElement element) => (bool)element.GetValue(SlideProperty);

    public static void SetSlide(UIElement element, bool value) => element.SetValue(SlideProperty, value);

    public static bool GetShowSlide(UIElement element) => (bool)element.GetValue(ShowSlideProperty);

    public static void SetShowSlide(UIElement element, bool value) => element.SetValue(ShowSlideProperty, value);

    private static void OnChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var element = (UIElement)sender;
        var compositor = ElementCompositionPreview.GetElementVisual(element).Compositor;
        var showsOnly = GetShowSlide(element);
        var slides = GetSlide(element) || showsOnly;
        var fades = GetFade(element) || slides;

        // An element's Translation is only there to animate once it has been asked for.
        if (slides)
            ElementCompositionPreview.SetIsTranslationEnabled(element, true);

        var show = fades ? CreateAnimation(compositor, isShowing: true, slides) : null;
        var hide = fades && !showsOnly ? CreateAnimation(compositor, isShowing: false, slides) : null;
        ElementCompositionPreview.SetImplicitShowAnimation(element, show);
        ElementCompositionPreview.SetImplicitHideAnimation(element, hide);
    }

    private static CompositionAnimationGroup CreateAnimation(Compositor compositor, bool isShowing, bool slides)
    {
        var animation = compositor.CreateAnimationGroup();
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.Target = nameof(Visual.Opacity);
        fade.Duration = Duration;
        fade.InsertKeyFrame(0f, isShowing ? 0 : 1);
        fade.InsertKeyFrame(1f, isShowing ? 1 : 0);
        animation.Add(fade);

        if (slides)
        {
            var slide = compositor.CreateScalarKeyFrameAnimation();
            slide.Target = "Translation.X";
            slide.Duration = Duration;
            slide.InsertKeyFrame(0f, isShowing ? -SlideDistance : 0);
            slide.InsertKeyFrame(1f, isShowing ? 0 : -SlideDistance);
            animation.Add(slide);
        }
        return animation;
    }
}
