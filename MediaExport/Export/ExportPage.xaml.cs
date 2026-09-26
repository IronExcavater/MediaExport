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
using System.Collections.Generic;
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

    public static readonly DependencyProperty SelectedClipProperty = DependencyProperty.Register(
        nameof(SelectedClip), typeof(int), typeof(ExportPage),
        new PropertyMetadata(-1, (page, _) => ((ExportPage)page).OnSelectedClipChanged()));

    private string? _dismissedLink;

    public ExportPage()
    {
        InitializeComponent();
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ExportViewModel.Media))
                Raise(nameof(HasCreatorLink));
        };
        ViewModel.Clips.CollectionChanged += (_, _) => ShowSelectedClipTimes();
        Search.Recents.CollectionChanged += (_, _) => Raise(nameof(HasRecents));
        Search.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SearchViewModel.IsPlayerObscured))
                Raise(nameof(IsPlayerObscured));
        };
        Loaded += (_, _) => CheckClipboard();
        App.Current.MainWindow.Activated += (_, _) => CheckClipboard();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ExportViewModel ViewModel { get; } = App.Current.Services.GetRequiredService<ExportViewModel>();
    public SearchViewModel Search { get; } = App.Current.Services.GetRequiredService<SearchViewModel>();
    public ActivityViewModel Activity { get; } = App.Current.Services.GetRequiredService<ActivityViewModel>();

    private static MainWindow Shell => (MainWindow)App.Current.MainWindow;

    public int SelectedClip
    {
        get => (int)GetValue(SelectedClipProperty);
        set => SetValue(SelectedClipProperty, value);
    }

    public bool HasSelectedClip => SelectedClip >= 0 && SelectedClip < ViewModel.Clips.Count;
    public bool HasRecents => Search.Recents.Count > 0;
    public bool HasCreatorLink => ViewModel.Media?.CreatorUrl is not null;
    public bool IsPlayerObscured => Search.IsPlayerObscured;

    /// <summary>A link on the clipboard that could be exported, offered on the home page.</summary>
    public string? ClipboardLink { get; private set; }

    public static string FormatDuration(TimeSpan? duration) => duration is { } time ? ExportViewModel.Format(time) : string.Empty;

    public static Visibility ShowDuration(TimeSpan? duration) => duration is null ? Visibility.Collapsed : Visibility.Visible;

    public static string ChapterCount(IReadOnlyList<Chapter> chapters) => $"Splits it into {chapters.Count} files, named after the chapters";

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

    private void Timeline_SeekRequested(object? sender, TimeSpan time) => player.Seek(time);

    // Side by side, the player gets the height the details and clips don't need; stacked, the page scrolls instead.
    private void Stage_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        bool sideBySide = panelColumn.Width.Value > 0;
        double available = editor.ActualHeight - editor.Padding.Top - editor.Padding.Bottom
            - details.ActualHeight - clipsCard.ActualHeight - stage.RowSpacing * 2;
        player.MaxHeight = sideBySide ? Math.Max(160, available) : double.PositiveInfinity;
    }

    // Start here: moves the selected clip's start, or starts a new clip that runs to the next one (or the end).
    private void MarkStart_Click(object sender, RoutedEventArgs args)
    {
        TimeSpan at = player.Position;
        if (HasSelectedClip)
        {
            Clip clip = ViewModel.Clips[SelectedClip];
            if (at < clip.End - TimeSpan.FromSeconds(1) && at >= Floor(SelectedClip))
                ViewModel.Clips[SelectedClip] = clip with { Start = at };
            return;
        }

        if (ViewModel.Clips.Any(clip => clip.Start <= at && at < clip.End))
            return;
        TimeSpan end = ViewModel.Clips.Where(clip => clip.Start > at).Select(clip => clip.Start).DefaultIfEmpty(ViewModel.Duration).Min();
        if (end - at >= TimeSpan.FromSeconds(1))
            SelectedClip = timeline.Insert(new Clip(at, end));
    }

    // End here: moves the selected clip's end, or makes a clip from the previous one (or the start) to here.
    private void MarkEnd_Click(object sender, RoutedEventArgs args)
    {
        TimeSpan at = player.Position;
        if (HasSelectedClip)
        {
            Clip clip = ViewModel.Clips[SelectedClip];
            if (at > clip.Start + TimeSpan.FromSeconds(1) && at <= Ceiling(SelectedClip))
                ViewModel.Clips[SelectedClip] = clip with { End = at };
            return;
        }

        if (ViewModel.Clips.Any(clip => clip.Start < at && at <= clip.End))
            return;
        TimeSpan start = ViewModel.Clips.Where(clip => clip.End < at).Select(clip => clip.End).DefaultIfEmpty(TimeSpan.Zero).Max();
        if (at - start >= TimeSpan.FromSeconds(1))
            SelectedClip = timeline.Insert(new Clip(start, at));
    }

    private void RemoveClip_Click(object sender, RoutedEventArgs args)
    {
        if (!HasSelectedClip)
            return;

        ViewModel.Clips.RemoveAt(SelectedClip);
        SelectedClip = -1;
    }

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
        if (!HasSelectedClip)
            return;

        if (ParseTime(clipStart.Text) is { } start && ParseTime(clipEnd.Text) is { } end
            && end - start >= TimeSpan.FromSeconds(1) && start >= Floor(SelectedClip) && end <= Ceiling(SelectedClip))
        {
            ViewModel.Clips[SelectedClip] = new Clip(start, end);
        }

        ShowSelectedClipTimes();
    }

    private TimeSpan Floor(int index) => index > 0 ? ViewModel.Clips[index - 1].End : TimeSpan.Zero;

    private TimeSpan Ceiling(int index) => index < ViewModel.Clips.Count - 1 ? ViewModel.Clips[index + 1].Start : ViewModel.Duration;

    private void OnSelectedClipChanged()
    {
        Raise(nameof(HasSelectedClip));
        ShowSelectedClipTimes();
    }

    private void ShowSelectedClipTimes()
    {
        Raise(nameof(HasSelectedClip));
        if (!HasSelectedClip)
            return;

        clipStart.Text = FormatPrecise(ViewModel.Clips[SelectedClip].Start);
        clipEnd.Text = FormatPrecise(ViewModel.Clips[SelectedClip].End);
    }

    private static string FormatPrecise(TimeSpan time) =>
        time.ToString(time.TotalHours >= 1 ? @"h\:mm\:ss\.f" : @"m\:ss\.f", CultureInfo.InvariantCulture);

    internal static TimeSpan? ParseTime(string text) =>
        TimeSpan.TryParseExact(text.Trim(), TimeFormats, CultureInfo.InvariantCulture, out TimeSpan time) ? time : null;

    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
