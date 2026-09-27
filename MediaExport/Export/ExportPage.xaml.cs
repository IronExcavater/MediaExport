// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Activity;
using MediaExport.Core.Exporting;
using MediaExport.Core.Media.Models;
using MediaExport.Search;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace MediaExport.Export;

public sealed partial class ExportPage : Page, INotifyPropertyChanged
{
    private static readonly string[] TimeFormats = [@"%s", @"m\:ss", @"mm\:ss", @"h\:mm\:ss", @"m\:ss\.f", @"mm\:ss\.f", @"h\:mm\:ss\.f", @"%s\.f"];

    private string? _dismissedLink;

    public ExportPage()
    {
        InitializeComponent();
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ExportViewModel.SelectedClip))
                ShowSelectedClipTimes();
        };
        ViewModel.Clips.CollectionChanged += (_, _) => ShowSelectedClipTimes();
        Loaded += (_, _) => CheckClipboard();
        App.Current.MainWindow.Activated += (_, _) => CheckClipboard();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ExportViewModel ViewModel { get; } = App.Current.Services.GetRequiredService<ExportViewModel>();
    public SearchViewModel Search { get; } = App.Current.Services.GetRequiredService<SearchViewModel>();
    public ActivityViewModel Activity { get; } = App.Current.Services.GetRequiredService<ActivityViewModel>();

    private static MainWindow Shell => (MainWindow)App.Current.MainWindow;

    public bool HasSelectedClip => ViewModel.SelectedClip >= 0 && ViewModel.SelectedClip < ViewModel.Clips.Count;

    /// <summary>A link on the clipboard that could be exported, offered on the home page.</summary>
    public string? ClipboardLink { get; private set; }

    public static string FormatDuration(TimeSpan? duration) => duration is { } time ? ExportViewModel.Format(time) : string.Empty;

    public static Visibility ShowDuration(TimeSpan? duration) => duration is null ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Settings for a part that's switched off stay in place, faded, so nothing moves.</summary>
    public static double Dim(bool isOn) => isOn ? 1 : 0.4;

    public static int JoinIndex(bool join) => join ? 0 : 1;

    public void SetJoinIndex(int index) => ViewModel.JoinClips = index != 1;

    private async void CheckClipboard()
    {
        try
        {
            DataPackageView content = Clipboard.GetContent();
            string? text = content.Contains(StandardDataFormats.Text) ? (await content.GetTextAsync()).Trim() : null;
            string? link = text is not null && SearchViewModel.IsLink(text) && text != _dismissedLink && text != ViewModel.Media?.SourceUrl ? text : null;
            if (link != ClipboardLink)
            {
                ClipboardLink = link;
                Raise(nameof(ClipboardLink));
            }
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            // The clipboard is unavailable while another app holds it; it's checked again next time.
        }
    }

    private async void OpenClipboardLink_Click(object sender, RoutedEventArgs args)
    {
        if (ClipboardLink is not { } link)
            return;

        _dismissedLink = link;
        ClipboardLink = null;
        Raise(nameof(ClipboardLink));
        await Shell.OpenLinkAsync(link);
    }

    private void Recent_ItemClick(object sender, ItemClickEventArgs args) => Shell.Open((MediaSearchResult)args.ClickedItem);

    private void SeeAllExports_Click(object sender, RoutedEventArgs args) => Shell.GoToActivity();

    private void StartSearching_Click(object sender, RoutedEventArgs args) => Shell.FocusSearch();

    private async void Creator_Click(object sender, RoutedEventArgs args)
    {
        if (Uri.TryCreate(ViewModel.Media?.CreatorUrl, UriKind.Absolute, out Uri? uri))
            await Launcher.LaunchUriAsync(uri);
    }

    private async void OpenInBrowser_Click(object sender, RoutedEventArgs args)
    {
        if (Uri.TryCreate(ViewModel.Media?.SourceUrl, UriKind.Absolute, out Uri? uri))
            await Launcher.LaunchUriAsync(uri);
    }

    private void CopyLink_Click(object sender, RoutedEventArgs args)
    {
        if (ViewModel.Media is not { } media)
            return;

        var package = new DataPackage();
        package.SetText(media.SourceUrl);
        Clipboard.SetContent(package);
        _dismissedLink = media.SourceUrl;
    }

    private void Close_Click(object sender, RoutedEventArgs args) => ViewModel.Close();

    private async void ChangeFolder_Click(object sender, RoutedEventArgs args)
    {
        if (await App.PickFolderAsync() is { } folder)
            ViewModel.OutputFolder = folder;
    }

    // MenuFlyout has no ItemsSource, so the languages are listed as it opens; each stays ticked or not.
    private void Subtitles_Opening(object? sender, object args)
    {
        var menu = (MenuFlyout)sender!;
        menu.Items.Clear();
        foreach (SubtitleChoice choice in ViewModel.Subtitles)
        {
            var item = new ToggleMenuFlyoutItem { Text = choice.Track.Name, IsChecked = choice.IsSelected };
            item.Click += (_, _) => choice.IsSelected = item.IsChecked;
            menu.Items.Add(item);
        }
    }

    private void Timeline_SeekRequested(object? sender, TimeSpan time) => player.Seek(time);

    private void AddClip_Click(object sender, RoutedEventArgs args) => ViewModel.AddClipAtCommand.Execute(player.Position);

    private void ClipItem_Click(object sender, RoutedEventArgs args)
    {
        int index = (int)((FrameworkElement)sender).Tag;
        ViewModel.SelectedClip = index;
        player.Seek(ViewModel.Clips[index].Start);
    }

    // With the panel beside it, the player gets the height the details and clips don't need, and the panel
    // fits the window; stacked, everything scrolls instead.
    private void Layout_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        bool sideBySide = panelColumn.Width.Value > 0;
        double viewport = editorScroller.ViewportHeight - editor.Padding.Top - editor.Padding.Bottom;
        double available = viewport - details.ActualHeight - clipsCard.ActualHeight - stage.RowSpacing * 2;
        player.MaxHeight = sideBySide ? Math.Max(200, available) : double.PositiveInfinity;
        panel.MaxHeight = sideBySide ? Math.Max(320, viewport) : double.PositiveInfinity;
    }

    private void MarkStart_Click(object sender, RoutedEventArgs args)
    {
        if (HasSelectedClip)
            ViewModel.TrySetSelectedClip(player.Position, ViewModel.Clips[ViewModel.SelectedClip].End);
    }

    private void MarkEnd_Click(object sender, RoutedEventArgs args)
    {
        if (HasSelectedClip)
            ViewModel.TrySetSelectedClip(ViewModel.Clips[ViewModel.SelectedClip].Start, player.Position);
    }

    private void RemoveClip_Click(object sender, RoutedEventArgs args) => ViewModel.RemoveClipCommand.Execute(ViewModel.SelectedClip);

    private void ClipTime_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Enter)
        {
            ApplyClipTimes();
            args.Handled = true;
        }
    }

    private void ClipTime_LostFocus(object sender, RoutedEventArgs args) => ApplyClipTimes();

    // Typed times replace the clip's when they make sense; otherwise the boxes go back to what it was.
    private void ApplyClipTimes()
    {
        if (ParseTime(clipStart.Text) is { } start && ParseTime(clipEnd.Text) is { } end)
            ViewModel.TrySetSelectedClip(start, end);

        ShowSelectedClipTimes();
    }

    private void ShowSelectedClipTimes()
    {
        Raise(nameof(HasSelectedClip));
        if (!HasSelectedClip)
            return;

        clipStart.Text = FormatPrecise(ViewModel.Clips[ViewModel.SelectedClip].Start);
        clipEnd.Text = FormatPrecise(ViewModel.Clips[ViewModel.SelectedClip].End);
    }

    private static string FormatPrecise(TimeSpan time) =>
        time.ToString(time.TotalHours >= 1 ? @"h\:mm\:ss\.f" : @"m\:ss\.f", CultureInfo.InvariantCulture);

    internal static TimeSpan? ParseTime(string text) =>
        TimeSpan.TryParseExact(text.Trim(), TimeFormats, CultureInfo.InvariantCulture, out TimeSpan time) ? time : null;

    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
