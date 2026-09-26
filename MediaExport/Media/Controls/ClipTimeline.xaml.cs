// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Exporting;
using MediaExport.Core.Media.Models;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using Windows.Foundation;
using Windows.System;

namespace MediaExport.Media.Controls;

/// <summary>
/// The media's length as a track: click to seek, drag across it to make a clip, drag a clip to move it,
/// drag its ends to trim it, click a chapter to keep just that chapter. Delete removes the selected clip.
/// </summary>
public sealed partial class ClipTimeline : UserControl
{
    private const double ChapterTop = 0, ChapterHeight = 20, ChapterBand = 26;
    private const double TrackHeight = 44;
    private const double RulerHeight = 22;
    private const double HandleWidth = 10;
    private const double DragThreshold = 4;
    private static readonly TimeSpan MinimumClip = TimeSpan.FromSeconds(1);

    public static readonly DependencyProperty DurationProperty = Register(nameof(Duration), typeof(TimeSpan), TimeSpan.Zero);
    public static readonly DependencyProperty ClipsProperty = Register(nameof(Clips), typeof(ObservableCollection<Clip>), null);
    public static readonly DependencyProperty ChaptersProperty = Register(nameof(Chapters), typeof(IReadOnlyList<Chapter>), null);
    public static readonly DependencyProperty PositionProperty = Register(nameof(Position), typeof(TimeSpan), TimeSpan.Zero);
    public static readonly DependencyProperty SelectedIndexProperty = Register(nameof(SelectedIndex), typeof(int), -1);

    private enum DragMode { None, Create, Move, Start, End }

    private DragMode _drag;
    private Point _pressedAt;
    private Clip _original;
    private int _dragIndex = -1;
    private bool _isDragging;

    public ClipTimeline()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
    }

    public TimeSpan Duration { get => (TimeSpan)GetValue(DurationProperty); set => SetValue(DurationProperty, value); }
    public ObservableCollection<Clip>? Clips { get => (ObservableCollection<Clip>?)GetValue(ClipsProperty); set => SetValue(ClipsProperty, value); }
    public IReadOnlyList<Chapter>? Chapters { get => (IReadOnlyList<Chapter>?)GetValue(ChaptersProperty); set => SetValue(ChaptersProperty, value); }

    /// <summary>The playhead, usually the player's position.</summary>
    public TimeSpan Position { get => (TimeSpan)GetValue(PositionProperty); set => SetValue(PositionProperty, value); }

    /// <summary>The clip being edited, or -1.</summary>
    public int SelectedIndex { get => (int)GetValue(SelectedIndexProperty); set => SetValue(SelectedIndexProperty, value); }

    /// <summary>Someone clicked a point in time, e.g. to move the player there.</summary>
    public event EventHandler<TimeSpan>? SeekRequested;

    private static DependencyProperty Register(string name, Type type, object? defaultValue) =>
        DependencyProperty.Register(name, type, typeof(ClipTimeline), new PropertyMetadata(defaultValue, OnPropertyChanged));

    private static void OnPropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var timeline = (ClipTimeline)sender;
        if (args.Property == ClipsProperty)
        {
            if (args.OldValue is ObservableCollection<Clip> old)
                old.CollectionChanged -= timeline.OnClipsChanged;
            if (args.NewValue is ObservableCollection<Clip> clips)
                clips.CollectionChanged += timeline.OnClipsChanged;
        }

        timeline.Render();
    }

    private void OnClipsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (SelectedIndex >= (Clips?.Count ?? 0))
            SelectedIndex = -1;
        if (!_isDragging)
            Render();
    }

    private void Surface_SizeChanged(object sender, SizeChangedEventArgs args) => Render();

    private double SurfaceWidth => Math.Max(1, surface.ActualWidth);

    // The chapter band only takes room when there are chapters.
    private double TrackTop => Chapters is { Count: > 0 } ? ChapterBand : 0;
    private double RulerTop => TrackTop + TrackHeight + 4;

    private double ToX(TimeSpan time) => Duration <= TimeSpan.Zero ? 0 : Math.Clamp(time / Duration, 0, 1) * SurfaceWidth;

    private TimeSpan ToTime(double x) =>
        Duration <= TimeSpan.Zero ? TimeSpan.Zero : TimeSpan.FromSeconds(Math.Round(Math.Clamp(x / SurfaceWidth, 0, 1) * Duration.TotalSeconds, 1));

    private void Render()
    {
        surface.Children.Clear();
        surface.Height = RulerTop + RulerHeight;
        double width = SurfaceWidth;

        Add(new Border { Style = StyleOf("TrackStyle"), Width = width, Height = TrackHeight }, 0, TrackTop);

        foreach (Chapter chapter in Chapters ?? [])
        {
            double left = ToX(chapter.Start), right = ToX(chapter.End);
            var segment = new Border
            {
                Style = StyleOf("ChapterStyle"),
                Width = Math.Max(1, right - left),
                Height = ChapterHeight,
                Child = new TextBlock { Style = (Style)Resources["ChapterTextStyle"], Text = chapter.Title }
            };
            ToolTipService.SetToolTip(segment, $"{chapter.Title} · {Format(chapter.Start)}–{Format(chapter.End)}\nClick to keep just this chapter");
            Add(segment, left, ChapterTop);
        }

        IReadOnlyList<Clip> clips = Clips ?? (IReadOnlyList<Clip>)[];
        for (int index = 0; index < clips.Count; index++)
        {
            Clip clip = clips[index];
            bool selected = index == SelectedIndex;
            double left = ToX(clip.Start), right = ToX(clip.End);
            var body = new Border
            {
                Style = StyleOf(selected ? "SelectedClipStyle" : "ClipStyle"),
                Width = Math.Max(HandleWidth * 2, right - left),
                Height = TrackHeight
            };
            ToolTipService.SetToolTip(body, $"{Format(clip.Start)}–{Format(clip.End)} ({Format(clip.Length)})");
            Add(body, left, TrackTop);

            if (selected)
            {
                Add(new Border { Style = StyleOf("HandleStyle"), Width = 3, Height = TrackHeight - 20 }, left + 4, TrackTop + 10);
                Add(new Border { Style = StyleOf("HandleStyle"), Width = 3, Height = TrackHeight - 20 }, Math.Max(left + 8, right - 7), TrackTop + 10);
            }
        }

        DrawRuler(width);

        if (Duration > TimeSpan.Zero)
            Add(new Border { Style = StyleOf("PlayheadStyle"), Width = 2, Height = TrackHeight + 8 }, ToX(Position) - 1, TrackTop - 4);
    }

    // Labels every "nice" step that leaves roughly 80px between them.
    private void DrawRuler(double width)
    {
        if (Duration <= TimeSpan.Zero)
            return;

        int[] steps = [1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600];
        double pixelsPerSecond = width / Duration.TotalSeconds;
        int step = steps.FirstOrDefault(candidate => candidate * pixelsPerSecond >= 80, steps[^1]);
        for (int second = 0; second <= Duration.TotalSeconds; second += step)
        {
            double x = second * pixelsPerSecond;
            Add(new Border { Style = StyleOf("TickStyle"), Width = 1, Height = 4 }, x, RulerTop);
            if (x < width - 30)
                Add(new TextBlock { Style = (Style)Resources["RulerTextStyle"], Text = Format(TimeSpan.FromSeconds(second)) }, x + 3, RulerTop + 2);
        }
    }

    private Style StyleOf(string key) => (Style)Resources[key];

    private void Add(FrameworkElement element, double left, double top)
    {
        element.IsHitTestVisible = false; // The surface handles all input.
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
        surface.Children.Add(element);
    }

    private (DragMode Mode, int Index) HitTest(Point point)
    {
        if (point.Y < TrackTop - 4 || point.Y > TrackTop + TrackHeight + 4 || Clips is null)
            return (DragMode.None, -1);

        for (int index = Clips.Count - 1; index >= 0; index--)
        {
            double left = ToX(Clips[index].Start), right = ToX(Clips[index].End);
            if (Math.Abs(point.X - left) <= HandleWidth / 2 + 2)
                return (DragMode.Start, index);
            if (Math.Abs(point.X - right) <= HandleWidth / 2 + 2)
                return (DragMode.End, index);
            if (point.X > left && point.X < right)
                return (DragMode.Move, index);
        }

        return (DragMode.Create, -1);
    }

    private void Surface_PointerPressed(object sender, PointerRoutedEventArgs args)
    {
        if (Duration <= TimeSpan.Zero || Clips is null)
            return;

        Focus(FocusState.Pointer);
        _pressedAt = args.GetCurrentPoint(surface).Position;
        _isDragging = false;

        if (_pressedAt.Y < TrackTop - 4)
        {
            _drag = DragMode.None;
            KeepChapterAt(_pressedAt.X);
            return;
        }

        (_drag, _dragIndex) = HitTest(_pressedAt);
        if (_dragIndex >= 0)
        {
            _original = Clips[_dragIndex];
            SelectedIndex = _dragIndex;
            Render();
        }

        surface.CapturePointer(args.Pointer);
        args.Handled = true;
    }

    private void Surface_PointerMoved(object sender, PointerRoutedEventArgs args)
    {
        Point point = args.GetCurrentPoint(surface).Position;
        if (_drag == DragMode.None || Clips is null)
        {
            ProtectedCursor = InputSystemCursor.Create(HitTest(point).Mode switch
            {
                DragMode.Start or DragMode.End => InputSystemCursorShape.SizeWestEast,
                DragMode.Move => InputSystemCursorShape.SizeAll,
                _ => InputSystemCursorShape.Arrow
            });
            return;
        }

        if (!_isDragging && Math.Abs(point.X - _pressedAt.X) < DragThreshold)
            return;
        _isDragging = true;

        TimeSpan at = ToTime(point.X);
        (TimeSpan floor, TimeSpan ceiling) = Neighbours(_dragIndex);
        switch (_drag)
        {
            case DragMode.Create:
                TimeSpan anchor = ToTime(_pressedAt.X);
                (floor, ceiling) = Gap(anchor);
                var created = new Clip(Clamp(Min(anchor, at), floor, ceiling), Clamp(Max(anchor, at), floor, ceiling));
                if (_dragIndex < 0)
                {
                    _dragIndex = Insert(created);
                    SelectedIndex = _dragIndex;
                }
                else
                {
                    Clips[_dragIndex] = created;
                }

                break;
            case DragMode.Move:
                TimeSpan offset = at - ToTime(_pressedAt.X);
                offset = Clamp(offset, floor - _original.Start, ceiling - _original.End);
                Clips[_dragIndex] = new Clip(_original.Start + offset, _original.End + offset);
                break;
            case DragMode.Start:
                Clips[_dragIndex] = _original with { Start = Clamp(at, floor, _original.End - MinimumClip) };
                break;
            case DragMode.End:
                Clips[_dragIndex] = _original with { End = Clamp(at, _original.Start + MinimumClip, ceiling) };
                break;
        }

        Render();
    }

    private void Surface_PointerReleased(object sender, PointerRoutedEventArgs args)
    {
        surface.ReleasePointerCaptures();
        if (_drag == DragMode.None)
            return;

        if (!_isDragging && _drag is DragMode.Create or DragMode.Move)
        {
            if (_drag == DragMode.Create)
                SelectedIndex = -1;
            SeekRequested?.Invoke(this, ToTime(_pressedAt.X));
        }

        // A drag too short to be a clip is dropped rather than kept as a sliver.
        if (_drag == DragMode.Create && _dragIndex >= 0 && Clips![_dragIndex].Length < MinimumClip)
        {
            Clips.RemoveAt(_dragIndex);
            SelectedIndex = -1;
        }

        _drag = DragMode.None;
        _dragIndex = -1;
        _isDragging = false;
        Render();
    }

    private void Surface_PointerExited(object sender, PointerRoutedEventArgs args)
    {
        if (_drag == DragMode.None)
            ProtectedCursor = null;
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key is VirtualKey.Delete or VirtualKey.Back && Clips is not null && SelectedIndex >= 0)
        {
            Clips.RemoveAt(SelectedIndex);
            SelectedIndex = -1;
            args.Handled = true;
        }
    }

    /// <summary>Adds the chapter under <paramref name="x"/> as a clip, or removes it if it's already one.</summary>
    private void KeepChapterAt(double x)
    {
        TimeSpan at = ToTime(x);
        if (Clips is null || Chapters?.FirstOrDefault(candidate => candidate.Start <= at && at < candidate.End) is not { } chapter)
            return;

        var clip = new Clip(chapter.Start, chapter.End);
        int existing = Clips.IndexOf(clip);
        if (existing >= 0)
        {
            Clips.RemoveAt(existing);
            SelectedIndex = -1;
            return;
        }

        // Anything overlapping the chapter gives way to it.
        foreach (Clip overlapping in Clips.Where(other => other.Start < clip.End && clip.Start < other.End).ToArray())
            Clips.Remove(overlapping);
        SelectedIndex = Insert(clip);
        Render();
    }

    /// <summary>Adds a clip in time order.</summary>
    public int Insert(Clip clip)
    {
        ObservableCollection<Clip> clips = Clips ?? throw new InvalidOperationException("The timeline has no clips to add to.");
        int index = clips.TakeWhile(other => other.Start < clip.Start).Count();
        clips.Insert(index, clip);
        return index;
    }

    // The free space either side of a clip, so clips never overlap.
    private (TimeSpan Floor, TimeSpan Ceiling) Neighbours(int index) =>
        (index > 0 ? Clips![index - 1].End : TimeSpan.Zero,
         index >= 0 && index < Clips!.Count - 1 ? Clips[index + 1].Start : Duration);

    private (TimeSpan Floor, TimeSpan Ceiling) Gap(TimeSpan at) =>
        (Clips!.Where(clip => clip.End <= at).Select(clip => clip.End).DefaultIfEmpty(TimeSpan.Zero).Max(),
         Clips!.Where(clip => clip.Start >= at).Select(clip => clip.Start).DefaultIfEmpty(Duration).Min());

    private static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max) => value < min ? min : value > max ? max : value;
    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;
    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    internal static string Format(TimeSpan time) =>
        time.ToString(time.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture);
}
