// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Media.Core;

namespace MediaExport.Media.Controls;

public sealed partial class HeroMediaView : MediaViewBase
{
    private const string PlayerHostName = "player.mediaexport.example";
    private static readonly Uri BlankPage = new("about:blank");

    public static readonly DependencyProperty AudioSourceProperty = DependencyProperty.Register(
        nameof(AudioSource),
        typeof(Uri),
        typeof(HeroMediaView),
        new PropertyMetadata(null, (view, _) => ((HeroMediaView)view).UpdateAudio()));

    public static readonly DependencyProperty IsAudioLoadingProperty = DependencyProperty.Register(
        nameof(IsAudioLoading),
        typeof(bool),
        typeof(HeroMediaView),
        new PropertyMetadata(false, (view, _) => ((HeroMediaView)view).UpdateAudio()));

    private Task? _playerReady;

    public HeroMediaView()
    {
        InitializeComponent();
        UpdateAudio();
    }

    /// <summary>A directly playable stream for audio media.</summary>
    public Uri? AudioSource
    {
        get => (Uri?)GetValue(AudioSourceProperty);
        set => SetValue(AudioSourceProperty, value);
    }

    public bool IsAudioLoading
    {
        get => (bool)GetValue(IsAudioLoadingProperty);
        set => SetValue(IsAudioLoadingProperty, value);
    }

    public string DurationSuffix => DurationText.Length > 0 ? $"  ·  {DurationText}" : string.Empty;

    protected override void RefreshMediaBindings()
    {
        Bindings.Update();

        // One details block, placed under the video or beside the artwork.
        // (Parent is null until first load, so both hosts are cleared explicitly.)
        Grid host = IsAudio ? audioDetailsHost : videoDetailsHost;
        if (!host.Children.Contains(details))
        {
            audioDetailsHost.Children.Remove(details);
            videoDetailsHost.Children.Remove(details);
            host.Children.Add(details);
        }

        videoStage.Visibility = IsAudio ? Visibility.Collapsed : Visibility.Visible;
        audioStage.Visibility = IsAudio ? Visibility.Visible : Visibility.Collapsed;
        _ = ShowVideoAsync();
    }

    private void HeroMediaView_Loaded(object sender, RoutedEventArgs args) => _ = ShowVideoAsync();

    private void HeroMediaView_Unloaded(object sender, RoutedEventArgs args)
    {
        audioPlayer.MediaPlayer?.Pause();
        _ = ShowVideoAsync();
    }

    private async Task ShowVideoAsync()
    {
        _playerReady ??= InitializePlayerAsync();
        await _playerReady;

        videoPlayer.Source = IsLoaded && Media?.EmbedUri is Uri embed
            ? new Uri($"https://{PlayerHostName}/player.html?src={Uri.EscapeDataString(embed.AbsoluteUri)}")
            : BlankPage;
    }

    private async Task InitializePlayerAsync()
    {
        videoPlayer.DefaultBackgroundColor = Colors.Transparent;
        await videoPlayer.EnsureCoreWebView2Async();

        // YouTube rejects embeds without a Referer (error 153), so the player page is served from an https origin.
        videoPlayer.CoreWebView2.SetVirtualHostNameToFolderMapping(
            PlayerHostName,
            Path.Combine(AppContext.BaseDirectory, "Assets", "Player"),
            CoreWebView2HostResourceAccessKind.DenyCors);
        videoPlayer.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
    }

    private void UpdateAudio()
    {
        audioPlayer.Source = AudioSource is Uri source ? MediaSource.CreateFromUri(source) : null;
        audioPlayer.Visibility = AudioSource is null ? Visibility.Collapsed : Visibility.Visible;
        previewStatus.Visibility = AudioSource is null ? Visibility.Visible : Visibility.Collapsed;
        previewRing.IsActive = IsAudioLoading;
        previewRing.Visibility = IsAudioLoading ? Visibility.Visible : Visibility.Collapsed;
        previewStatusText.Text = IsAudioLoading ? "Loading preview" : "Preview unavailable";
    }

    private void VideoStage_SizeChanged(object sender, SizeChangedEventArgs args) =>
        videoStage.Height = args.NewSize.Width * 9 / 16;
}
