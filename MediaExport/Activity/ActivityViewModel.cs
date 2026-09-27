// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaExport.Core.Exporting;
using MediaExport.Core.Media.Models;
using MediaExport.Core.Settings;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace MediaExport.Activity;

/// <summary>Every export, newest first. Finished ones are kept between runs.</summary>
public sealed partial class ActivityViewModel : ObservableObject
{
    private const int HistoryLimit = 200;

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
        };
        SyncLatest();
    }

    public ObservableCollection<ExportJob> Jobs { get; } = [];

    /// <summary>The few newest exports, for the home page.</summary>
    public ObservableCollection<ExportJob> Latest { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRunning))]
    public partial int RunningCount { get; private set; }

    public bool HasJobs => Jobs.Count > 0;
    public bool HasRunning => RunningCount > 0;

    /// <summary>How far the running exports have got together, 0 to 100, for the navigation badge's ring.</summary>
    public double RunningProgress => Jobs.Where(job => job.IsRunning).Select(job => job.Progress).DefaultIfEmpty(0).Average();

    /// <summary>True while no running export has a percentage yet.</summary>
    public bool IsRunningProgressUnknown => Jobs.Where(job => job.IsRunning).All(job => job.IsIndeterminate);

    // ponytail: exports run side by side with no queue; add one if people start many at once.
    public ExportJob Start(MediaSearchResult media, ExportOptions options)
    {
        var job = new ExportJob(media, options);
        Jobs.Insert(0, job);
        _ = RunAsync(job);
        return job;
    }

    /// <summary>The newest export of <paramref name="media"/> that's still running, if any.</summary>
    public ExportJob? FindRunning(MediaSearchResult media) =>
        Jobs.FirstOrDefault(job => job.IsRunning && job.Media.SourceUrl == media.SourceUrl);

    /// <summary>Removes exports from the list (running ones are cancelled). Files are left alone.</summary>
    public void Remove(IEnumerable<ExportJob> jobs)
    {
        foreach (ExportJob job in jobs.ToArray())
        {
            job.CancelCommand.Execute(null);
            Jobs.Remove(job);
        }

        SaveHistory();
    }

    private bool CanClearFinished() => Jobs.Any(job => job.IsFinished);

    [RelayCommand(CanExecute = nameof(CanClearFinished))]
    private void ClearFinished() => Remove(Jobs.Where(job => job.IsFinished));

    private async Task RunAsync(ExportJob job)
    {
        void OnJobChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            OnPropertyChanged(nameof(RunningProgress));
            OnPropertyChanged(nameof(IsRunningProgressUnknown));
        }

        job.PropertyChanged += OnJobChanged;
        RunningCount++;
        try
        {
            await job.RunAsync(_downloader);
        }
        finally
        {
            RunningCount--;
            job.PropertyChanged -= OnJobChanged;
        }

        SaveHistory();
        ClearFinishedCommand.NotifyCanExecuteChanged();
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

    private void SaveHistory()
    {
        _settings.Current.History = Jobs.Where(job => job.IsFinished).Take(HistoryLimit).Select(job => job.ToRecord()).ToList();
        _settings.Save();
    }
}
