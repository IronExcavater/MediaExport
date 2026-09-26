// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
using System.Threading.Tasks;

namespace MediaExport.Media.Controls;

public sealed partial class HeroMediaView : MediaViewBase
{
    private const string PlayerHostName = "player.mediaexport.example";
    private const double MaxAudioStageHeight = 360;
    private static readonly Uri BlankPage = new("about:blank");

    public static readonly DependencyProperty IsPlayerHiddenProperty = DependencyProperty.Register(
        nameof(IsPlayerHidden),
        typeof(bool),
        typeof(HeroMediaView),
        new PropertyMetadata(false, (view, args) =>
            ((HeroMediaView)view).player.Visibility = (bool)args.NewValue ? Visibility.Collapsed : Visibility.Visible));

    private Task? _playerReady;

    public HeroMediaView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Hides the web player behind its thumbnail. WebView2 intercepts pointer input meant for
    /// popups drawn over it (and can hang doing so), so set this while one is open.
    /// </summary>
    public bool IsPlayerHidden
    {
        get => (bool)GetValue(IsPlayerHiddenProperty);
        set => SetValue(IsPlayerHiddenProperty, value);
    }

    public string DurationSuffix => DurationText.Length > 0 ? $"  ·  {DurationText}" : string.Empty;

    protected override void RefreshMediaBindings()
    {
        Bindings.Update();
        UpdateStageHeight(stage.ActualWidth);
        _ = ShowPlayerAsync();
    }

    private void HeroMediaView_Loaded(object sender, RoutedEventArgs args) => _ = ShowPlayerAsync();

    // Navigating away stops playback; the page is cached, so it would otherwise keep playing.
    private void HeroMediaView_Unloaded(object sender, RoutedEventArgs args) => _ = ShowPlayerAsync();

    private async Task ShowPlayerAsync()
    {
        _playerReady ??= InitializePlayerAsync();
        await _playerReady;

        player.Source = IsLoaded && Media?.EmbedUri is Uri embed
            ? new Uri($"https://{PlayerHostName}/player.html?src={Uri.EscapeDataString(embed.AbsoluteUri)}")
            : BlankPage;
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
    }

    private void Stage_SizeChanged(object sender, SizeChangedEventArgs args) => UpdateStageHeight(args.NewSize.Width);

    // Video is 16:9; the audio widget is artwork behind a waveform and doesn't need to be as tall.
    private void UpdateStageHeight(double width)
    {
        double height = width * 9 / 16;
        stage.Height = IsAudio ? Math.Min(height, MaxAudioStageHeight) : height;
    }
}
