// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using Windows.Foundation;

namespace MediaExport.Media.Controls;

/// <summary>A media action that preserves text selection without losing ordinary click and keyboard activation.</summary>
public sealed class MediaButton : Button
{
    private const double DragThreshold = 4;
    private Point _pressedAt;
    private bool _pointerPressed;
    private bool _pointerDragged;

    public MediaButton()
    {
        base.Click += ForwardClick;
    }

    /// <summary>Raised for keyboard and ordinary pointer activation, but not for a drag that selected text.</summary>
    public new event RoutedEventHandler? Click;

    protected override void OnPointerPressed(PointerRoutedEventArgs args)
    {
        _pointerPressed = true;
        _pointerDragged = false;
        _pressedAt = args.GetCurrentPoint(this).Position;
        base.OnPointerPressed(args);
    }

    protected override void OnPointerMoved(PointerRoutedEventArgs args)
    {
        if (_pointerPressed)
        {
            var position = args.GetCurrentPoint(this).Position;
            _pointerDragged |= Math.Abs(position.X - _pressedAt.X) >= DragThreshold
                || Math.Abs(position.Y - _pressedAt.Y) >= DragThreshold;
        }

        base.OnPointerMoved(args);
    }

    protected override void OnPointerReleased(PointerRoutedEventArgs args)
    {
        base.OnPointerReleased(args);
        _pointerPressed = false;
        _pointerDragged = false;
    }

    protected override void OnPointerCanceled(PointerRoutedEventArgs args)
    {
        _pointerPressed = false;
        _pointerDragged = false;
        base.OnPointerCanceled(args);
    }

    private void ForwardClick(object sender, RoutedEventArgs args)
    {
        if (!_pointerDragged || !HasSelectedText(Content as DependencyObject))
            Click?.Invoke(this, args);
    }

    private static bool HasSelectedText(DependencyObject? element)
    {
        if (element is TextBlock { IsTextSelectionEnabled: true, SelectedText.Length: > 0 })
            return true;

        if (element is null)
            return false;

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
        {
            if (HasSelectedText(VisualTreeHelper.GetChild(element, index)))
                return true;
        }

        return false;
    }
}
