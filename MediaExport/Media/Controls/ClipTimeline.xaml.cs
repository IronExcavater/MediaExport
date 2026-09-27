// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Converters;
using MediaExport.Core.Exporting;
using MediaExport.Core.Media.Models;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
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
/// The media's length, from left to right. Clips are the parts kept: drag across the track to add one,
/// drag a clip to move it and its ends to trim it. Chapters sit above; click one to keep it.
/// The ruler below moves the player, as do the arrow keys. Delete removes the selected clip, Escape lets go of it.
/// </summary>
public sealed partial class ClipTimeline : UserControl
{
    private const double ChapterHeight = 22, ChapterGap = 6;
    private const double TrackHeight = 52;
    private const double RulerHeight = 22;
    private const double EdgeReach = 8;
    private const double DragThreshold = 4;
    private const double SeekStep = 5;
    private static readonly TimeSpan MinimumClip = TimeSpan.FromSeconds(1);

    public static readonly DependencyProperty DurationProperty = Register(nameof(Duration), typeof(TimeSpan), TimeSpan.Zero);
    public static readonly DependencyProperty ClipsProperty = Register(nameof(Clips), typeof(ObservableCollection<Clip>), null);
    public static readonly DependencyProperty ChaptersProperty = Register(nameof(Chapters), typeof(IReadOnlyList<Chapter>), null);
    public static readonly DependencyProperty SelectedIndexProperty = Register(nameof(SelectedIndex), typeof(int), -1);

    public static readonly DependencyProperty PositionProperty = DependencyProperty.Register(
        nameof(Position), typeof(TimeSpan), typeof(ClipTimeline),
        new PropertyMetadata(TimeSpan.Zero, (timeline, _) => ((ClipTimeline)timeline).PlacePlayhead()));

    private enum Part { None, Chapter, Track, Clip, ClipStart, ClipEnd, Ruler }

    private readonly Border _playhead;
    private readonly Border _hoverLine;
    private readonly ToolTip _tip = new() { Placement = Microsoft.UI.Xaml.Controls.Primitives.PlacementMode.Top };
    private Part _drag;
    private Point _pressedAt;
    private Clip _original;
    private int _dragIndex = -1;
    private int _hoveredChapter = -1;
    private bool _isDragging;

    public ClipTimeline()
    {
        InitializeComponent();
        _playhead = new Border { Style = StyleOf("PlayheadStyle"), Width = 2, IsHitTestVisible = false };
        _hoverLine = new Border { Style = StyleOf("HoverStyle"), Width = 1, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        ToolTipService.SetToolTip(surface, _tip);
        KeyDown += OnKeyDown;
    }

    public TimeSpan Duration { get => (TimeSpan)GetValue(DurationProperty); set => SetValue(DurationProperty, value); }
    public ObservableCollection<Clip>? Clips { get => (ObservableCollection<Clip>?)GetValue(ClipsProperty); set => SetValue(ClipsProperty, value); }
    public IReadOnlyList<Chapter>? Chapters { get => (IReadOnlyList<Chapter>?)GetValue(ChaptersProperty); set => SetValue(ChaptersProperty, value); }

    /// <summary>The playhead, usually the player's position.</summary>
    public TimeSpan Position { get => (TimeSpan)GetValue(PositionProperty); set => SetValue(PositionProperty, value); }

    /// <summary>The clip being edited, or -1.</summary>
    public int SelectedIndex { get => (int)GetValue(SelectedIndexProperty); set => SetValue(SelectedIndexProperty, value); }

    /// <summary>Someone picked a point in time, to move the player there.</summary>
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
        if (!_isDragging)
            Render();
    }

    private void Surface_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        surface.Clip = new RectangleGeometry { Rect = new Rect(0, 0, args.NewSize.Width, args.NewSize.Height) };
        Render();
    }

    private double SurfaceWidth => Math.Max(1, surface.ActualWidth);
    private bool HasChapters => Chapters is { Count: > 0 };
    private double TrackTop => HasChapters ? ChapterHeight + ChapterGap : 0;
    private double RulerTop => TrackTop + TrackHeight;
    private double ToX(TimeSpan time) => Duration <= TimeSpan.Zero ? 0 : Math.Clamp(time / Duration, 0, 1) * SurfaceWidth;

    private TimeSpan ToTime(double x) =>
        Duration <= TimeSpan.Zero ? TimeSpan.Zero : TimeSpan.FromSeconds(Math.Round(Math.Clamp(x / SurfaceWidth, 0, 1) * Duration.TotalSeconds, 1));

    private void Render()
    {
        surface.Children.Clear();
        surface.Height = RulerTop + RulerHeight;
        double width = SurfaceWidth;
        IReadOnlyList<Clip> clips = Clips ?? (IReadOnlyList<Clip>)[];

        IReadOnlyList<Chapter> chapters = Chapters ?? [];
        for (int index = 0; index < chapters.Count; index++)
        {
            double left = ToX(chapters[index].Start), right = ToX(chapters[index].End);
            Add(new Border
            {
                Style = StyleOf(index == _hoveredChapter ? "HoveredChapterStyle" : "ChapterStyle"),
                Width = Math.Max(2, right - left - 2),
                Height = ChapterHeight,
                Child = new TextBlock { Style = StyleOf("ChapterTextStyle"), Text = chapters[index].Title }
            }, left + 1, 0);
        }

        Add(new Border { Style = StyleOf("TrackStyle"), Width = width, Height = TrackHeight }, 0, TrackTop);

        // With clips, the rest is dimmed: what's kept is what's bright.
        if (clips.Count > 0)
        {
            double from = 0;
            foreach (Clip clip in clips.Append(new Clip(Duration, Duration)))
            {
                double to = ToX(clip.Start);
                if (to - from > 1)
                    Add(new Border { Style = StyleOf("DroppedStyle"), Width = to - from, Height = TrackHeight }, from, TrackTop);
                from = ToX(clip.End);
            }
        }

        for (int index = 0; index < clips.Count; index++)
            DrawClip(clips[index], index, index == SelectedIndex);

        DrawRuler(width);

        _hoverLine.Height = RulerTop + 6 - TrackTop;
        Add(_hoverLine, 0, TrackTop, hitTest: false);
        _playhead.Height = RulerTop + 6;
        Add(_playhead, 0, 0, hitTest: false);
        PlacePlayhead();
    }

    private void DrawClip(Clip clip, int index, bool selected)
    {
        double left = ToX(clip.Start), right = ToX(clip.End), width = Math.Max(6, right - left);
        var label = new TextBlock { Style = StyleOf("ClipTextStyle"), Text = width > 70 ? $"{index + 1}   {Format.Time(clip.Length)}" : $"{index + 1}" };
        Add(new Border { Style = StyleOf(selected ? "SelectedClipStyle" : "ClipStyle"), Width = width, Height = TrackHeight, Child = width > 16 ? label : null }, left, TrackTop);

        // Grips show which clip the handles belong to, and that its ends can be dragged.
        if (selected && width > 20)
        {
            Add(new Border { Style = StyleOf("GripStyle"), Width = 3, Height = TrackHeight - 24 }, left + 4, TrackTop + 12);
            Add(new Border { Style = StyleOf("GripStyle"), Width = 3, Height = TrackHeight - 24 }, right - 7, TrackTop + 12);
        }
    }

    // Labels every "nice" step that leaves at least 70px between them.
    private void DrawRuler(double width)
    {
        if (Duration <= TimeSpan.Zero)
            return;

        int[] steps = [1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600];
        double pixelsPerSecond = width / Duration.TotalSeconds;
        int step = steps.FirstOrDefault(candidate => candidate * pixelsPerSecond >= 70, steps[^1]);

        // The length is always shown at the end; step labels stop short of it.
        var end = new TextBlock { Style = StyleOf("RulerTextStyle"), Text = Format.Time(Duration) };
        end.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double endLeft = width - end.DesiredSize.Width;
        Add(end, endLeft, RulerTop + 3);

        for (int second = 0; second <= Duration.TotalSeconds; second += step)
        {
            double x = second * pixelsPerSecond;
            Add(new Border { Style = StyleOf("TickStyle"), Width = 1, Height = 5 }, x, RulerTop + 2);
            var label = new TextBlock { Style = StyleOf("RulerTextStyle"), Text = Format.Time(TimeSpan.FromSeconds(second)) };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            if (x + 4 + label.DesiredSize.Width + 12 <= endLeft)
                Add(label, x + 4, RulerTop + 3);
        }
    }

    private void PlacePlayhead()
    {
        Canvas.SetLeft(_playhead, Math.Clamp(ToX(Position) - 1, 0, Math.Max(0, SurfaceWidth - 2)));
        _playhead.Visibility = Duration > TimeSpan.Zero ? Visibility.Visible : Visibility.Collapsed;
    }

    private Style StyleOf(string key) => (Style)Resources[key];

    private void Add(FrameworkElement element, double left, double top, bool hitTest = false)
    {
        element.IsHitTestVisible = hitTest; // The surface handles all input.
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
        surface.Children.Add(element);
    }

    private (Part Part, int Index) HitTest(Point point)
    {
        if (point.Y >= RulerTop)
            return (Part.Ruler, -1);

        if (point.Y < TrackTop)
        {
            TimeSpan at = ToTime(point.X);
            int chapter = Chapters?.ToList().FindIndex(item => item.Start <= at && at < item.End) ?? -1;
            return chapter >= 0 ? (Part.Chapter, chapter) : (Part.None, -1);
        }

        IReadOnlyList<Clip> clips = Clips ?? (IReadOnlyList<Clip>)[];
        // The selected clip's ends win over a neighbour's, then any clip's ends, then bodies.
        foreach (int index in Enumerable.Range(0, clips.Count).OrderBy(index => index == SelectedIndex ? 0 : 1))
        {
            double left = ToX(clips[index].Start), right = ToX(clips[index].End);
            if (Math.Abs(point.X - left) <= EdgeReach)
                return (Part.ClipStart, index);
            if (Math.Abs(point.X - right) <= EdgeReach)
                return (Part.ClipEnd, index);
        }

        for (int index = 0; index < clips.Count; index++)
        {
            if (point.X > ToX(clips[index].Start) && point.X < ToX(clips[index].End))
                return (Part.Clip, index);
        }

        return (Part.Track, -1);
    }

    private void Surface_PointerPressed(object sender, PointerRoutedEventArgs args)
    {
        if (Duration <= TimeSpan.Zero || Clips is null)
            return;

        Focus(FocusState.Pointer);
        _pressedAt = args.GetCurrentPoint(surface).Position;
        _isDragging = false;
        (_drag, _dragIndex) = HitTest(_pressedAt);

        switch (_drag)
        {
            case Part.Chapter:
                KeepChapter(_dragIndex);
                _drag = Part.None;
                return;
            case Part.Ruler:
                SeekRequested?.Invoke(this, ToTime(_pressedAt.X));
                break;
            case Part.Clip or Part.ClipStart or Part.ClipEnd:
                _original = Clips[_dragIndex];
                SelectedIndex = _dragIndex;
                Render();
                break;
            case Part.Track:
                _dragIndex = -1;
                break;
        }

        surface.CapturePointer(args.Pointer);
        args.Handled = true;
    }

    private void Surface_PointerMoved(object sender, PointerRoutedEventArgs args)
    {
        Point point = args.GetCurrentPoint(surface).Position;
        if (_drag == Part.None || Clips is null)
        {
            ShowHover(point);
            return;
        }

        if (_drag == Part.Ruler)
        {
            SeekRequested?.Invoke(this, ToTime(point.X));
            return;
        }

        if (!_isDragging && Math.Abs(point.X - _pressedAt.X) < DragThreshold)
            return;
        _isDragging = true;

        TimeSpan at = ToTime(point.X);
        switch (_drag)
        {
            case Part.Track:
                TimeSpan anchor = ToTime(_pressedAt.X);
                (TimeSpan floor, TimeSpan ceiling) = Gap(anchor);
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
            case Part.Clip:
                (TimeSpan low, TimeSpan high) = Neighbours(_dragIndex);
                TimeSpan offset = Clamp(at - ToTime(_pressedAt.X), low - _original.Start, high - _original.End);
                Clips[_dragIndex] = new Clip(_original.Start + offset, _original.End + offset);
                break;
            case Part.ClipStart:
                Clips[_dragIndex] = _original with { Start = Clamp(at, Neighbours(_dragIndex).Floor, _original.End - MinimumClip) };
                break;
            case Part.ClipEnd:
                Clips[_dragIndex] = _original with { End = Clamp(at, _original.Start + MinimumClip, Neighbours(_dragIndex).Ceiling) };
                break;
        }

        Render();
        ShowTip(point, _dragIndex >= 0 && _dragIndex < Clips.Count ? $"{Format.Time(Clips[_dragIndex].Start)} – {Format.Time(Clips[_dragIndex].End)}" : Format.Time(at));
    }

    private void Surface_PointerReleased(object sender, PointerRoutedEventArgs args)
    {
        surface.ReleasePointerCaptures();
        if (_drag is Part.None or Part.Ruler)
        {
            _drag = Part.None;
            return;
        }

        // A click without a drag moves the player there, and on empty track lets go of the selection.
        if (!_isDragging)
        {
            if (_drag == Part.Track)
                SelectedIndex = -1;
            SeekRequested?.Invoke(this, _drag == Part.Clip && Clips is not null ? Clips[_dragIndex].Start : ToTime(_pressedAt.X));
        }

        // A drag too short to be a clip is dropped rather than kept as a sliver.
        if (_drag == Part.Track && _dragIndex >= 0 && Clips![_dragIndex].Length < MinimumClip)
        {
            Clips.RemoveAt(_dragIndex);
            SelectedIndex = -1;
        }

        _drag = Part.None;
        _dragIndex = -1;
        _isDragging = false;
        Render();
    }

    private void Surface_PointerExited(object sender, PointerRoutedEventArgs args)
    {
        if (_drag != Part.None)
            return;

        ProtectedCursor = null;
        _hoverLine.Visibility = Visibility.Collapsed;
        _tip.IsOpen = false;
        if (_hoveredChapter >= 0)
        {
            _hoveredChapter = -1;
            Render();
        }
    }

    // What's under the pointer: the cursor says what dragging does, the tip says when and what it is.
    private void ShowHover(Point point)
    {
        (Part part, int index) = HitTest(point);
        ProtectedCursor = InputSystemCursor.Create(part switch
        {
            Part.ClipStart or Part.ClipEnd => InputSystemCursorShape.SizeWestEast,
            Part.Clip => InputSystemCursorShape.SizeAll,
            Part.Chapter or Part.Ruler => InputSystemCursorShape.Hand,
            _ => InputSystemCursorShape.Cross
        });

        int chapter = part == Part.Chapter ? index : -1;
        if (chapter != _hoveredChapter)
        {
            _hoveredChapter = chapter;
            Render();
        }

        TimeSpan at = ToTime(point.X);
        _hoverLine.Visibility = part is Part.Track or Part.Ruler ? Visibility.Visible : Visibility.Collapsed;
        Canvas.SetLeft(_hoverLine, point.X);

        string text = part switch
        {
            Part.Chapter => $"{Chapters![index].Title}, {Format.Time(Chapters[index].Start)} – {Format.Time(Chapters[index].End)}. Click to keep just this chapter.",
            Part.Clip or Part.ClipStart or Part.ClipEnd => $"Clip {index + 1}, {Format.Time(Clips![index].Start)} – {Format.Time(Clips[index].End)}",
            Part.Track => $"{Format.Time(at)}. Drag to keep a part.",
            Part.Ruler => $"{Format.Time(at)}. Click to play from here.",
            _ => string.Empty
        };
        ShowTip(point, text);
    }

    private void ShowTip(Point point, string text)
    {
        _tip.Content = text;
        _tip.PlacementRect = new Rect(point.X, point.Y < TrackTop ? 0 : TrackTop, 1, 1);
        _tip.IsOpen = text.Length > 0;
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key is VirtualKey.Delete or VirtualKey.Back && Clips is not null && SelectedIndex >= 0 && SelectedIndex < Clips.Count)
        {
            Clips.RemoveAt(SelectedIndex);
            SelectedIndex = -1;
            args.Handled = true;
        }
        else if (args.Key == VirtualKey.Escape && SelectedIndex >= 0)
        {
            SelectedIndex = -1;
            args.Handled = true;
        }
        else if (args.Key is VirtualKey.Left or VirtualKey.Right && Duration > TimeSpan.Zero)
        {
            TimeSpan step = TimeSpan.FromSeconds(args.Key == VirtualKey.Left ? -SeekStep : SeekStep);
            SeekRequested?.Invoke(this, Clamp(Position + step, TimeSpan.Zero, Duration));
            args.Handled = true;
        }
    }

    /// <summary>Adds a chapter as a clip, or removes it if it's already one. Anything overlapping it gives way.</summary>
    private void KeepChapter(int index)
    {
        if (Clips is null || Chapters is null || index < 0)
            return;

        var clip = new Clip(Chapters[index].Start, Chapters[index].End);
        int existing = Clips.IndexOf(clip);
        if (existing >= 0)
        {
            Clips.RemoveAt(existing);
            SelectedIndex = -1;
            return;
        }

        foreach (Clip overlapping in Clips.Where(other => other.Start < clip.End && clip.Start < other.End).ToArray())
            Clips.Remove(overlapping);
        SelectedIndex = Insert(clip);
        Render();
    }

    private int Insert(Clip clip)
    {
        ObservableCollection<Clip> clips = Clips!;
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

}
