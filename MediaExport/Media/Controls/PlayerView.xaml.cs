// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using System;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Windows.Storage.Streams;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Foundation;

namespace MediaExport.Media.Controls;

/// <summary>Plays media in its site's own player, and reports and moves the playhead.</summary>
public sealed partial class PlayerView : MediaViewBase
{
    private const string PlayerHostName = "player.mediaexport.example";
    private static readonly Uri BlankPage = new("about:blank");

    public static readonly DependencyProperty IsPlayerHiddenProperty = DependencyProperty.Register(
        nameof(IsPlayerHidden), typeof(bool), typeof(PlayerView),
        new PropertyMetadata(false, (view, args) => _ = ((PlayerView)view).SetHiddenAsync((bool)args.NewValue)));

    public static readonly DependencyProperty PositionProperty = DependencyProperty.Register(
        nameof(Position), typeof(TimeSpan), typeof(PlayerView), new PropertyMetadata(TimeSpan.Zero));

    private Task? _playerReady;
    private Uri? _shown;

    public PlayerView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Swaps the web player for a still of itself. WebView2 swallows input meant for popups drawn
    /// over it (and can hang doing so), so this is set while one is open; the still keeps it looking the same.
    /// </summary>
    public bool IsPlayerHidden
    {
        get => (bool)GetValue(IsPlayerHiddenProperty);
        set => SetValue(IsPlayerHiddenProperty, value);
    }

    /// <summary>Where the player is, as it plays.</summary>
    public TimeSpan Position
    {
        get => (TimeSpan)GetValue(PositionProperty);
        private set => SetValue(PositionProperty, value);
    }

    public bool IsPlaying { get; private set; }

    public void Seek(TimeSpan time)
    {
        Position = time;
        Post($$"""{"type":"seek","seconds":{{time.TotalSeconds.ToString(CultureInfo.InvariantCulture)}}}""");
    }

    public void TogglePlayback() => Post(IsPlaying ? """{"type":"pause"}""" : """{"type":"play"}""");

    // Always 16:9, as big as the space allows.
    protected override Size MeasureOverride(Size available)
    {
        double width = double.IsInfinity(available.Width) ? 640 : available.Width;
        double height = width * 9 / 16;
        if (!double.IsInfinity(available.Height) && height > available.Height)
        {
            height = available.Height;
            width = height * 16 / 9;
        }

        var size = new Size(width, height);
        base.MeasureOverride(size);
        return size;
    }

    protected override void RefreshMediaBindings()
    {
        Bindings.Update();
        Position = TimeSpan.Zero;
        IsPlaying = false;
        _ = ShowPlayerAsync();
    }

    private void PlayerView_Loaded(object sender, RoutedEventArgs args) => _ = ShowPlayerAsync();

    // Navigating away stops playback; the page is cached, so it would otherwise keep playing.
    private void PlayerView_Unloaded(object sender, RoutedEventArgs args) => _ = ShowPlayerAsync();

    private async Task ShowPlayerAsync()
    {
        _playerReady ??= InitializePlayerAsync();
        await _playerReady;

        Uri next = IsLoaded && Media?.EmbedUri is Uri embed
            ? new Uri($"https://{PlayerHostName}/player.html?src={Uri.EscapeDataString(embed.AbsoluteUri)}")
            : BlankPage;

        // Setting the same source again reloads the player, so it's skipped.
        if (next == _shown)
            return;

        _shown = next;
        player.CoreWebView2.Navigate(next.AbsoluteUri);
    }

    private async Task InitializePlayerAsync()
    {
        player.DefaultBackgroundColor = Colors.Transparent;
        await player.EnsureCoreWebView2Async();

        // YouTube rejects embeds without a Referer (error 153), so the player page is served from an https origin.
        player.CoreWebView2.SetVirtualHostNameToFolderMapping(
            PlayerHostName,
            Path.Combine(AppContext.BaseDirectory, "Assets", "Player"),
            CoreWebView2HostResourceAccessKind.DenyCors);
        player.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        player.CoreWebView2.Settings.AreDevToolsEnabled = false;
        player.CoreWebView2.Settings.IsStatusBarEnabled = false;
    }

    private async Task SetHiddenAsync(bool hidden)
    {
        if (!hidden)
        {
            player.Visibility = Visibility.Visible;
            still.Visibility = Visibility.Collapsed;
            return;
        }

        if (player.CoreWebView2 is not null && _shown != BlankPage)
        {
            try
            {
                using var stream = new InMemoryRandomAccessStream();
                await player.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
                var image = new BitmapImage();
                stream.Seek(0);
                await image.SetSourceAsync(stream);
                still.Source = image;
            }
            catch (Exception exception) when (exception is COMException or InvalidOperationException)
            {
                still.Source = null; // The thumbnail underneath shows instead.
            }
        }

        // The popup may have closed while the still was taken.
        if (IsPlayerHidden)
        {
            still.Visibility = Visibility.Visible;
            player.Visibility = Visibility.Collapsed;
        }
    }

    private void Post(string json)
    {
        if (player.CoreWebView2 is not null && _shown != BlankPage)
            player.CoreWebView2.PostWebMessageAsJson(json);
    }

    private void Player_WebMessageReceived(WebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        using JsonDocument message = JsonDocument.Parse(args.WebMessageAsJson);
        JsonElement root = message.RootElement;
        if (root.TryGetProperty("type", out JsonElement type) && type.GetString() == "time")
        {
            Position = TimeSpan.FromSeconds(root.GetProperty("seconds").GetDouble());
            IsPlaying = root.GetProperty("playing").GetBoolean();
        }
    }
}
