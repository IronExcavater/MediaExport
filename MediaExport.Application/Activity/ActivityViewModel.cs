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

namespace MediaExport.Activity;

/// <summary>Exports listed together on the Activity page, under a heading such as "Up next" or "Yesterday".</summary>
public sealed class JobGroup(string title, IEnumerable<ExportJob> jobs) : List<ExportJob>(jobs)
{
    public string Title => title;
}

/// <summary>Every export: those waiting and running, and the finished ones, which are kept between runs.</summary>
public sealed partial class ActivityViewModel : ObservableObject
{
    // Two at a time keeps each one quick. The rest wait their turn.
    private const int ConcurrentLimit = 2;

    // How many finished exports are kept between runs.
    private const int HistoryCapacity = 100;

    private readonly IMediaDownloader _downloader;
    private readonly SettingsStore _settings;

    public ActivityViewModel(IMediaDownloader downloader, SettingsStore settings)
    {
        _downloader = downloader;
        _settings = settings;

        foreach (var record in settings.Current.History)
            Jobs.Add(new ExportJob(record));

        Jobs.CollectionChanged += (_, _) => OnJobsChanged();
        UpdateGroups();
    }

    /// <summary>Every export, newest first.</summary>
    public ObservableCollection<ExportJob> Jobs { get; } = [];

    /// <summary><see cref="Jobs"/> as the page lists them: what is exporting, what is up next, then by when they finished.</summary>
    public ObservableCollection<JobGroup> Groups { get; } = [];

    public bool HasJobs => Jobs.Count > 0;

    public IReadOnlyList<ExportJob> SelectedJobs { get; private set; } = [];
    public int SelectedCount => SelectedJobs.Count;
    public bool CanRetrySelected => SelectedJobs.Any(job => job.CanRetry);
    public bool CanCancelSelected => SelectedJobs.Any(job => job.IsActive);
    public bool CanRemoveSelected => SelectedJobs.Any(job => job.IsFinished);

    public void Select(IEnumerable<ExportJob> jobs)
    {
        SelectedJobs = jobs.Distinct().ToList();
        OnPropertyChanged(nameof(SelectedJobs));
        OnPropertyChanged(nameof(SelectedCount));
        RefreshSelectionEligibility();
    }

    private void RefreshSelectionEligibility()
    {
        OnPropertyChanged(nameof(CanRetrySelected));
        OnPropertyChanged(nameof(CanCancelSelected));
        OnPropertyChanged(nameof(CanRemoveSelected));
        RetrySelectedCommand.NotifyCanExecuteChanged();
        CancelSelectedCommand.NotifyCanExecuteChanged();
        RemoveSelectedCommand.NotifyCanExecuteChanged();
    }

    /// <summary>How many exports are waiting or running.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActive))]
    public partial int ActiveCount { get; private set; }

    public bool HasActive => ActiveCount > 0;

    /// <summary>How far the waiting and running exports have got together, from 0 to 100.</summary>
    public double ActiveProgress
    {
        get
        {
            var active = Jobs.Where(job => job.IsActive);
            var progress = active.Select(job => job.Progress);
            return progress.DefaultIfEmpty(0).Average();
        }
    }

    /// <summary>Whether none of them has a percentage yet, so there is nothing to show but that they are busy.</summary>
    public bool IsActiveProgressUnknown => ActiveProgress <= 0;

    /// <summary>Adds an export to the queue. It starts at once if fewer than the limit are running.</summary>
    public ExportJob Start(MediaSearchResult media, ExportOptions options)
    {
        var job = new ExportJob(media, options);
        job.PropertyChanged += Job_PropertyChanged;
        ActiveCount++;
        Jobs.Insert(0, job);
        StartNext();
        return job;
    }

    /// <summary>Takes an export off the list, stopping it first if it hasn't finished. Its files stay where they are.</summary>
    public void Remove(ExportJob job)
    {
        job.CancelCommand.Execute(null);
        Jobs.Remove(job);
    }

    public void Retry(ExportJob job)
    {
        if (!job.Retry())
            return;

        job.PropertyChanged += Job_PropertyChanged;
        ActiveCount++;
        UpdateGroups();
        SaveHistory();
        StartNext();
    }

    [RelayCommand(CanExecute = nameof(CanRetrySelected))]
    private void RetrySelected()
    {
        foreach (var job in SelectedJobs.Where(job => job.CanRetry).ToList())
            Retry(job);
        Select([]);
    }

    [RelayCommand(CanExecute = nameof(CanCancelSelected))]
    private void CancelSelected()
    {
        foreach (var job in SelectedJobs.Where(job => job.IsActive))
            job.CancelCommand.Execute(null);
    }

    [RelayCommand(CanExecute = nameof(CanRemoveSelected))]
    private void RemoveSelected()
    {
        foreach (var job in SelectedJobs.Where(job => job.IsFinished).ToList())
            Remove(job);
        Select([]);
    }

    private bool CanClearFinished() => Jobs.Any(job => job.IsFinished);

    /// <summary>Takes every finished export off the list. Their files stay where they are.</summary>
    [RelayCommand(CanExecute = nameof(CanClearFinished))]
    private void ClearFinished()
    {
        var finished = Jobs.Where(job => job.IsFinished).ToList();
        foreach (var job in finished)
            Jobs.Remove(job);
    }

    /// <summary>
    /// Sorts exports under headings: those exporting, those up next in the order they will start, then the
    /// finished ones by the day they finished, newest first.
    /// </summary>
    public static IReadOnlyList<JobGroup> Group(IReadOnlyCollection<ExportJob> jobs, DateTimeOffset now)
    {
        var running = jobs.Where(job => job.IsRunning);
        var queued = jobs.Where(job => job.IsQueued).Reverse();
        var finished = jobs.Where(job => job.IsFinished).OrderByDescending(job => job.FinishedAt);
        var finishedByDay = finished.GroupBy(job => GetHeading(job.FinishedAt ?? now, now));

        var groups = new List<JobGroup> { new("Exporting", running), new("Up next", queued) };
        groups.AddRange(finishedByDay.Select(day => new JobGroup(day.Key, day)));
        return groups.Where(group => group.Count > 0).ToList();
    }

    private static string GetHeading(DateTimeOffset finished, DateTimeOffset now)
    {
        var daysAgo = (now.LocalDateTime.Date - finished.LocalDateTime.Date).Days;
        return daysAgo switch
        {
            <= 0 => "Today",
            1 => "Yesterday",
            < 7 => "Earlier this week",
            < 31 => "Earlier this month",
            _ => "Older"
        };
    }

    // Starts waiting exports, oldest first, for as long as there is room for another to run.
    private void StartNext()
    {
        while (true)
        {
            var runningCount = Jobs.Count(job => job.IsRunning);
            var next = Jobs.LastOrDefault(job => job.IsQueued);
            if (runningCount >= ConcurrentLimit || next is null)
                return;

            // The job is running by the time this returns, and reports its end through its State.
            _ = next.RunAsync(_downloader);
        }
    }

    private void Job_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        var job = (ExportJob)sender!;
        OnPropertyChanged(nameof(ActiveProgress));
        OnPropertyChanged(nameof(IsActiveProgressUnknown));
        RefreshSelectionEligibility();
        if (args.PropertyName != nameof(ExportJob.State))
            return;

        UpdateGroups();
        if (job.IsActive)
            return;

        job.PropertyChanged -= Job_PropertyChanged;
        ActiveCount--;
        SaveHistory();
        ClearFinishedCommand.NotifyCanExecuteChanged();
        StartNext();
    }

    private void OnJobsChanged()
    {
        OnPropertyChanged(nameof(HasJobs));
        ClearFinishedCommand.NotifyCanExecuteChanged();
        UpdateGroups();
        SaveHistory();
    }

    private void UpdateGroups()
    {
        Groups.Clear();
        foreach (var group in Group(Jobs, DateTimeOffset.Now))
            Groups.Add(group);
    }

    public bool HasFinishedHistory => Jobs.Any(job => job.IsFinished);
    private void SaveHistory()
    {
        var finished = Jobs.Where(job => job.IsFinished).Take(HistoryCapacity);
        _settings.Current.History = finished.Select(job => job.ToRecord()).ToList();

        OnPropertyChanged(nameof(HasFinishedHistory));
        _settings.Save();
    }
}
