// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using System;
using System.Numerics;
using Windows.Foundation;

namespace MediaExport.Controls;

/// <summary>Shows an element through a mask: visible where the mask is opaque, hidden where it is transparent.</summary>
/// <remarks>
/// XAML has no opacity mask, so the target's own drawing is hidden and a masked copy of it is drawn in the
/// host instead. The target still receives all input. A WebView2 can't be copied, so it can't be masked.
/// </remarks>
/// <param name="host">The element the copy is drawn in. It should cover the target.</param>
/// <param name="mask">The element whose opacity is the mask. It must be inside the host.</param>
public sealed class OpacityMask(FrameworkElement host, FrameworkElement mask)
{
    private static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(160);

    private SpriteVisual? _copy;
    private bool _isApplied;

    // The number of the newest fade, so an older one that finishes late doesn't remove the copy.
    private int _fadeCount;

    /// <summary>The element to mask.</summary>
    public UIElement? Target
    {
        get;
        set
        {
            if (field == value)
                return;

            Remove();
            field = value;
        }
    }

    /// <summary>Whether the target is shown through the mask. Changing it fades between the two.</summary>
    public bool IsApplied
    {
        get => _isApplied;
        set
        {
            if (_isApplied == value || Target is null)
                return;

            _isApplied = value;
            CrossFade();
        }
    }

    /// <summary>Where the target's top-left corner is within the host.</summary>
    public Point Offset
    {
        get;
        set
        {
            field = value;
            _copy?.Offset = ToVector(value);
        }
    }

    /// <summary>Shows the target's own drawing again straight away.</summary>
    public void Remove()
    {
        _isApplied = false;
        _fadeCount++;
        if (Target is null)
            return;

        var target = ElementCompositionPreview.GetElementVisual(Target);
        target.StopAnimation(nameof(Visual.Opacity));
        target.Opacity = 1;

        ElementCompositionPreview.SetElementChildVisual(host, null);
        _copy?.Dispose();
        _copy = null;
    }

    private void CrossFade()
    {
        var target = ElementCompositionPreview.GetElementVisual(Target);
        if (_isApplied)
            _copy ??= CreateCopy(target);
        if (_copy is null)
            return;

        var isApplied = _isApplied;
        var fade = ++_fadeCount;

        var batch = target.Compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        Fade(target, to: isApplied ? 0 : 1);
        Fade(_copy, to: isApplied ? 1 : 0);
        batch.Completed += (_, _) =>
        {
            var isNewest = fade == _fadeCount;
            if (!isApplied && isNewest)
                Remove();
            batch.Dispose();
        };
        batch.End();
    }

    private SpriteVisual CreateCopy(Visual target)
    {
        var compositor = target.Compositor;
        var maskVisual = ElementCompositionPreview.GetElementVisual(mask);
        // The mask is only ever seen through the brush made from it.
        maskVisual.Opacity = 0;

        var brush = compositor.CreateMaskBrush();
        brush.Source = CreateLiveBrush(target);
        brush.Mask = CreateLiveBrush(maskVisual);

        var copy = compositor.CreateSpriteVisual();
        copy.Brush = brush;
        copy.Opacity = 0;
        copy.Offset = ToVector(Offset);
        copy.StartAnimation(nameof(Visual.Size), SizeOf(target));

        ElementCompositionPreview.SetElementChildVisual(host, copy);
        return copy;
    }

    // A brush that paints a visual as it looks right now, and keeps up with it.
    private static CompositionSurfaceBrush CreateLiveBrush(Visual visual)
    {
        var surface = visual.Compositor.CreateVisualSurface();
        surface.SourceVisual = visual;
        surface.StartAnimation(nameof(CompositionVisualSurface.SourceSize), SizeOf(visual));

        var brush = visual.Compositor.CreateSurfaceBrush(surface);
        brush.Stretch = CompositionStretch.Fill;
        return brush;
    }

    // An expression that always equals a visual's size.
    private static ExpressionAnimation SizeOf(Visual visual)
    {
        var size = visual.Compositor.CreateExpressionAnimation("visual.Size");
        size.SetReferenceParameter("visual", visual);
        return size;
    }

    private static void Fade(Visual visual, float to)
    {
        // Stopping leaves the opacity wherever an interrupted fade had got to, and this one starts from there.
        visual.StopAnimation(nameof(Visual.Opacity));

        var compositor = visual.Compositor;
        var easeOut = compositor.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1));
        var animation = compositor.CreateScalarKeyFrameAnimation();
        animation.Duration = FadeDuration;
        animation.StopBehavior = AnimationStopBehavior.LeaveCurrentValue;
        animation.InsertKeyFrame(0, visual.Opacity);
        animation.InsertKeyFrame(1, to, easeOut);
        visual.StartAnimation(nameof(Visual.Opacity), animation);
    }

    private static Vector3 ToVector(Point point) => new((float)point.X, (float)point.Y, 0);
}
