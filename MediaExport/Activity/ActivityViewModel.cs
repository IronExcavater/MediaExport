// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaExport.Core.Exporting;
using MediaExport.Core.Media.Models;
using MediaExport.Core.Settings;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace MediaExport.Activity;

/// <summary>Exports that belong together on the Activity page, under a heading such as "Up next" or "Yesterday".</summary>
public sealed class JobGroup(string title, IEnumerable<ExportJob> jobs) : List<ExportJob>(jobs)
{
    public string Title => title;
}

/// <summary>Every export, newest first: the ones waiting and running, then the finished ones, which are kept between runs.</summary>
public sealed partial class ActivityViewModel : ObservableObject
{
    private const int HistoryLimit = 200;

    // Two at a time keeps each one quick; the rest wait their turn.
    private const int AtOnce = 2;

    private readonly IMediaDownloader _downloader;
    private readonly SettingsStore _settings;

    public ActivityViewModel(IMediaDownloader downloader, SettingsStore settings)
    {
        _downloader = downloader;
        _settings = settings;
        foreach (ExportRecord record in settings.Current.History)
            Jobs.Add(new ExportJob(record));

        Jobs.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasJobs));
            ClearFinishedCommand.NotifyCanExecuteChanged();
            SyncLatest();
            SyncGroups();
        };
        SyncLatest();
        SyncGroups();
    }

    public ObservableCollection<ExportJob> Jobs { get; } = [];

    /// <summary>The few newest exports, for the home page.</summary>
    public ObservableCollection<ExportJob> Latest { get; } = [];

    /// <summary>The exports matching <see cref="Filter"/>, under headings: what's exporting, what's up next, then by when they finished.</summary>
    public ObservableCollection<JobGroup> Groups { get; } = [];

    /// <summary>Words to find among the exports' titles, makers and formats.</summary>
    [ObservableProperty]
    public partial string Filter { get; set; } = string.Empty;

    /// <summary>How many exports are under way or waiting.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRunning))]
    public partial int RunningCount { get; private set; }

    public bool HasJobs => Jobs.Count > 0;
    public bool HasRunning => RunningCount > 0;
    public bool HasMatches => Groups.Count > 0;

    /// <summary>How far the running exports have got together, 0 to 100, for the navigation badge's ring.</summary>
    public double RunningProgress => Jobs.Where(job => job.IsRunning).Select(job => job.Progress).DefaultIfEmpty(0).Average();

    /// <summary>True while no running export has a percentage yet.</summary>
    public bool IsRunningProgressUnknown => Jobs.Where(job => job.IsRunning).All(job => job.IsIndeterminate || job.IsQueued);

    /// <summary>Adds an export to the queue; it starts straight away if fewer than <see cref="AtOnce"/> are running.</summary>
    public ExportJob Start(MediaSearchResult media, ExportOptions options)
    {
        var job = new ExportJob(media, options);
        job.PropertyChanged += OnJobChanged;
        RunningCount++;
        Jobs.Insert(0, job);
        StartNext();
        return job;
    }

    /// <summary>The newest export of <paramref name="media"/> that's waiting or under way, if any.</summary>
    public ExportJob? FindRunning(MediaSearchResult media) =>
        Jobs.FirstOrDefault(job => job.IsRunning && job.Media.SourceUrl == media.SourceUrl);

    /// <summary>Removes exports from the list (waiting and running ones are cancelled). Files are left alone.</summary>
    public void Remove(IEnumerable<ExportJob> jobs)
    {
        foreach (ExportJob job in jobs.ToArray())
        {
            job.CancelCommand.Execute(null);
            Jobs.Remove(job);
        }

        SaveHistory();
    }

    /// <summary>
    /// Sorts exports under headings: those exporting, those up next (in the order they'll start), then the
    /// finished ones by how long ago, newest first. Only those saying every word of <paramref name="filter"/> are kept.
    /// </summary>
    public static IReadOnlyList<JobGroup> Group(IEnumerable<ExportJob> jobs, string filter, DateTimeOffset now)
    {
        string[] words = filter.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        ExportJob[] matching = jobs
            .Where(job => words.All(word => $"{job.Media.Title} {job.Media.Creator} {job.Summary}".Contains(word, StringComparison.CurrentCultureIgnoreCase)))
            .ToArray();

        var groups = new List<JobGroup>
        {
            new("Exporting", matching.Where(job => job is { IsRunning: true, IsQueued: false })),
            new("Up next", matching.Where(job => job.IsQueued).Reverse())
        };
        groups.AddRange(matching
            .Where(job => job.IsFinished)
            .OrderByDescending(job => job.FinishedAt)
            .GroupBy(job => Heading(job.FinishedAt ?? now, now))
            .Select(group => new JobGroup(group.Key, group)));
        return groups.Where(group => group.Count > 0).ToArray();
    }

    private static string Heading(DateTimeOffset finished, DateTimeOffset now)
    {
        int days = (now.LocalDateTime.Date - finished.LocalDateTime.Date).Days;
        return days switch
        {
            <= 0 => "Today",
            1 => "Yesterday",
            < 7 => "Earlier this week",
            < 31 => "Earlier this month",
            _ => "Older"
        };
    }

    partial void OnFilterChanged(string value) => SyncGroups();

    private bool CanClearFinished() => Jobs.Any(job => job.IsFinished);

    [RelayCommand(CanExecute = nameof(CanClearFinished))]
    private void ClearFinished() => Remove(Jobs.Where(job => job.IsFinished));

    // The oldest waiting export goes next, whenever there's room for it.
    private void StartNext()
    {
        while (Jobs.Count(job => job is { IsRunning: true, IsQueued: false }) < AtOnce && Jobs.LastOrDefault(job => job.IsQueued) is { } next)
            _ = next.RunAsync(_downloader);
    }

    private void OnJobChanged(object? sender, PropertyChangedEventArgs args)
    {
        var job = (ExportJob)sender!;
        OnPropertyChanged(nameof(RunningProgress));
        OnPropertyChanged(nameof(IsRunningProgressUnknown));
        if (args.PropertyName != nameof(ExportJob.State))
            return;

        SyncGroups();
        if (!job.IsFinished)
            return;

        job.PropertyChanged -= OnJobChanged;
        RunningCount--;
        SaveHistory();
        ClearFinishedCommand.NotifyCanExecuteChanged();
        StartNext();
    }

    private void SyncLatest()
    {
        ExportJob[] latest = Jobs.Take(4).ToArray();
        if (latest.SequenceEqual(Latest))
            return;

        Latest.Clear();
        foreach (ExportJob job in latest)
            Latest.Add(job);
    }

    private void SyncGroups()
    {
        Groups.Clear();
        foreach (JobGroup group in Group(Jobs, Filter, DateTimeOffset.Now))
            Groups.Add(group);
        OnPropertyChanged(nameof(HasMatches));
    }

    private void SaveHistory()
    {
        _settings.Current.History = Jobs.Where(job => job.IsFinished).Take(HistoryLimit).Select(job => job.ToRecord()).ToList();
        _settings.Save();
    }
}
