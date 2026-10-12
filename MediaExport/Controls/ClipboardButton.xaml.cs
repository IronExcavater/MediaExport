// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;

namespace MediaExport.Controls;

/// <summary>Copies a field's text if there is some, pastes if there isn't, and hides when it can do neither.</summary>
public sealed partial class ClipboardButton : UserControl
{
    private readonly ScalarTransition _iconFade = new() { Duration = VisibilityTransition.Duration };

    // Kept from the clipboard's last change, because reading it on every keystroke is slow.
    private bool _hasClipboardText;

    public ClipboardButton()
    {
        InitializeComponent();
        Visibility = Visibility.Collapsed;

        Loaded += (_, _) =>
        {
            Clipboard.ContentChanged += Clipboard_ContentChanged;
            ReadClipboard();
        };
        Unloaded += (_, _) => Clipboard.ContentChanged -= Clipboard_ContentChanged;
    }

    /// <summary>What is copied.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Whether there is anything to copy. The button copies when there is, and pastes when there isn't.</summary>
    public bool CanCopy
    {
        get;
        set
        {
            field = value;
            Update();
        }
    }

    /// <summary>Raised with the clipboard's text when it is pasted, for the field to take.</summary>
    public event EventHandler<string>? Pasted;

    private void Clipboard_ContentChanged(object? sender, object args) => DispatcherQueue.TryEnqueue(ReadClipboard);

    private void ReadClipboard()
    {
        try
        {
            _hasClipboardText = Clipboard.GetContent().Contains(StandardDataFormats.Text);
        }
        catch (Exception exception) when (IsClipboardUnavailable(exception))
        {
            // The clipboard may still hold text, so pasting stays on offer.
            _hasClipboardText = true;
        }

        Update();
    }

    private void Update()
    {
        var canCopy = CanCopy;
        var isUseful = canCopy || _hasClipboardText;

        var action = canCopy ? "Copy to clipboard" : "Paste from clipboard";
        AutomationProperties.SetName(button, action);
        ToolTipService.SetToolTip(button, action);

        // The icons cross-fade when the action changes, but a button only now appearing starts on the right one.
        var wasHidden = Visibility == Visibility.Collapsed;
        var iconFade = wasHidden ? null : _iconFade;
        copyIcon.OpacityTransition = iconFade;
        pasteIcon.OpacityTransition = iconFade;
        copyIcon.Opacity = canCopy ? 1 : 0;
        pasteIcon.Opacity = canCopy ? 0 : 1;

        Visibility = isUseful ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Button_Click(object sender, RoutedEventArgs args)
    {
        if (CanCopy)
            Copy_Click(sender, args);
        else
            Paste_Click(sender, args);
    }

    private void Menu_Opening(object? sender, object args)
    {
        copyItem.IsEnabled = CanCopy;
        pasteItem.IsEnabled = _hasClipboardText;
    }

    private void Copy_Click(object sender, RoutedEventArgs args)
    {
        var package = new DataPackage();
        package.SetText(Text);
        Clipboard.SetContent(package);
    }

    private async void Paste_Click(object sender, RoutedEventArgs args)
    {
        try
        {
            var content = Clipboard.GetContent();
            if (!content.Contains(StandardDataFormats.Text))
                return;

            var text = await content.GetTextAsync();
            Pasted?.Invoke(this, text);
        }
        catch (Exception exception) when (IsClipboardUnavailable(exception))
        {
            // The clipboard changed or was locked before it could be read, so nothing is pasted.
        }
    }

    // Thrown when another app has the clipboard locked, or this app is in the background.
    private static bool IsClipboardUnavailable(Exception exception) =>
        exception is COMException or UnauthorizedAccessException;
}
