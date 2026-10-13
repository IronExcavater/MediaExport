// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using MediaExport.Core.Media.Providers;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using System;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Foundation;

namespace MediaExport.Media.Controls;

/// <summary>
/// Plays one media result in its source's own player. What surrounds it can follow the playhead by binding to
/// <see cref="Position"/>, and move it with <see cref="Seek"/>.
/// </summary>
public sealed partial class PlayerMediaView : MediaView
{
    // The made-up address the player's page is served from. See InitializeWebViewAsync.
    private const string PlayerHostName = "player.mediaexport.example";
    private static readonly Uri BlankPage = new("about:blank");

    public static readonly DependencyProperty PositionProperty = DependencyProperty.Register(
        nameof(Position),
        typeof(TimeSpan),
        typeof(PlayerMediaView),
        new PropertyMetadata(TimeSpan.Zero));

    // Set up once, on first use. Every later use waits for it.
    private Task? _webViewReady;
    private Uri? _shownPage;
    private XamlRoot? _xamlRoot;

    public PlayerMediaView()
    {
        InitializeComponent();
    }

    /// <summary>Where the playhead is, kept up to date as the media plays. Bind to it one way.</summary>
    public TimeSpan Position
    {
        get => (TimeSpan)GetValue(PositionProperty);
        private set => SetValue(PositionProperty, value);
    }

    /// <summary>Moves the playhead.</summary>
    public void Seek(TimeSpan time)
    {
        Position = time;
        var seconds = time.TotalSeconds.ToString(CultureInfo.InvariantCulture);
        Post($$"""{"type":"seek","seconds":{{seconds}}}""");
    }

    // A 16:9 picture as large as the space allows. A source whose player is a strip gets that strip at full width.
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 640 : availableSize.Width;
        var height = width * 9 / 16;

        var stripHeight = EmbedLinks.HeightFor(Media?.ProviderId);
        if (stripHeight is not null)
        {
            height = Math.Min(stripHeight.Value, availableSize.Height);
        }
        else if (height > availableSize.Height)
        {
            height = availableSize.Height;
            width = height * 16 / 9;
        }

        var size = new Size(width, height);
        base.MeasureOverride(size);
        return size;
    }

    protected override void OnMediaChanged(MediaSearchResult? oldMedia, MediaSearchResult? newMedia)
    {
        InvalidateMeasure();
        Position = TimeSpan.Zero;
        _ = ShowPlayerAsync();
    }

    private void PlayerMediaView_Loaded(object sender, RoutedEventArgs args)
    {
        // The view has no XamlRoot once it is unloaded, so the one it listens to is kept to stop listening to.
        _xamlRoot = XamlRoot;
        _xamlRoot.Changed += XamlRoot_Changed;
        _ = ShowPlayerAsync();
    }

    // The page this view is on is cached when the user leaves it, so the player is emptied or it would play on.
    private void PlayerMediaView_Unloaded(object sender, RoutedEventArgs args)
    {
        if (_xamlRoot is not null)
            _xamlRoot.Changed -= XamlRoot_Changed;

        _xamlRoot = null;
        _ = ShowPlayerAsync();
    }

    // The display scale changes when the window is moved to another screen, and the clip is made for one scale.
    private void XamlRoot_Changed(XamlRoot sender, XamlRootChangedEventArgs args) => RoundWebView();

    private async Task ShowPlayerAsync()
    {
        _webViewReady ??= InitializeWebViewAsync();
        await _webViewReady;

        var embedLink = Media is null ? null : EmbedLinks.For(Media);
        var page = BlankPage;
        if (IsLoaded && embedLink is not null)
            page = new Uri($"https://{PlayerHostName}/player.html?src={Uri.EscapeDataString(embedLink.AbsoluteUri)}");

        // Navigating to the page already shown would restart what is playing.
        if (page == _shownPage)
            return;

        _shownPage = page;
        webView.CoreWebView2.Navigate(page.AbsoluteUri);
    }

    private async Task InitializeWebViewAsync()
    {
        webView.DefaultBackgroundColor = Colors.Transparent;
        await webView.EnsureCoreWebView2Async();

        // YouTube turns away an embed that is on a page without a web address (its error 153), so the player's
        // page in the app's folder is served as if it were on an https site.
        var core = webView.CoreWebView2;
        var playerFolder = Path.Combine(AppContext.BaseDirectory, "Assets", "Player");
        core.SetVirtualHostNameToFolderMapping(PlayerHostName, playerFolder, CoreWebView2HostResourceAccessKind.DenyCors);
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        RoundWebView();
    }

    private void WebView_SizeChanged(object sender, SizeChangedEventArgs args) => RoundWebView();

    // A web view isn't rounded by the CornerRadius of the grid it is in, and a playing video is drawn straight to
    // the screen, past any rounding its page has. The visual the web view draws in is clipped to the shape instead.
    private void RoundWebView()
    {
        var content = ElementCompositionPreview.GetElementChildVisual(webView);
        if (content is null || XamlRoot is null)
            return;

        // That visual is measured in screen pixels, not in the view pixels XAML uses.
        var scale = (float)XamlRoot.RasterizationScale;
        var width = (float)webView.ActualWidth * scale;
        var height = (float)webView.ActualHeight * scale;
        var radius = new Vector2((float)frame.CornerRadius.TopLeft * scale);
        content.Clip = content.Compositor.CreateRectangleClip(0, 0, width, height, radius, radius, radius, radius);
    }

    private void Post(string json)
    {
        var hasPlayer = webView.CoreWebView2 is not null && _shownPage != BlankPage;
        if (hasPlayer)
            webView.CoreWebView2!.PostWebMessageAsJson(json);
    }

    // The player's page says where the playhead is, and how far the wheel was turned over it.
    private void WebView_WebMessageReceived(WebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        using var message = JsonDocument.Parse(args.WebMessageAsJson);
        var root = message.RootElement;
        var type = root.TryGetProperty("type", out var typeProperty) ? typeProperty.GetString() : null;
        switch (type)
        {
            case "time":
                Position = TimeSpan.FromSeconds(root.GetProperty("seconds").GetDouble());
                break;
            case "scroll":
                ScrollPage(root.GetProperty("delta").GetDouble());
                break;
        }
    }

    // A web view keeps the wheel to itself, so the turns its page doesn't use are passed to the scroller around it.
    private void ScrollPage(double delta)
    {
        DependencyObject? ancestor = this;
        while (ancestor is not null and not ScrollViewer)
            ancestor = VisualTreeHelper.GetParent(ancestor);

        if (ancestor is ScrollViewer scroller)
            scroller.ChangeView(null, scroller.VerticalOffset + delta, null, disableAnimation: true);
    }
}
