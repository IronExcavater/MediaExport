// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Activity;
using MediaExport.Core.Exporting;
using MediaExport.Core.Media.Models;
using MediaExport.Core.Settings;
using MediaExport.Tests.Export;

namespace MediaExport.Tests.Activity;

public sealed class ActivityViewModelTests
{
    private static readonly MediaSearchResult Video = new("id", "https://example.test/media", null, "Result", "Creator", null);

    [Test]
    public async Task Finished_exports_are_kept_for_next_time()
    {
        var settings = new SettingsStore(null);
        var downloader = new ExportViewModelTests.FakeDownloader();
        var activity = new ActivityViewModel(downloader, settings);

        ExportJob job = activity.Start(Video, new ExportOptions("mp4", "out") { MaxHeight = 720 });
        downloader.Download.SetResult([@"C:\out\Result.mp4"]);
        await Task.Yield();
        var restored = new ActivityViewModel(downloader, settings);

        Assert.Multiple(() =>
        {
            Assert.That(job.IsCompleted, Is.True);
            Assert.That(activity.HasRunning, Is.False);
            Assert.That(restored.Jobs, Has.Count.EqualTo(1));
            Assert.That(restored.Jobs[0].IsCompleted, Is.True);
            Assert.That(restored.Jobs[0].Status, Is.EqualTo("Saved to out"));
            Assert.That(restored.Jobs[0].Summary, Is.EqualTo(".mp4 720p"));
        });
    }

    [Test]
    public void Two_exports_run_at_once_and_the_rest_wait_their_turn()
    {
        var activity = new ActivityViewModel(new ExportViewModelTests.FakeDownloader(), new SettingsStore(null));
        var options = new ExportOptions("mp3", "out") { IncludeVideo = false };

        ExportJob first = activity.Start(Video, options), second = activity.Start(Video, options), third = activity.Start(Video, options);
        bool[] waiting = [first.IsQueued, second.IsQueued, third.IsQueued];
        string[] headings = activity.Groups.Select(group => $"{group.Title}: {group.Count}").ToArray();
        third.CancelCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(waiting, Is.EqualTo(new[] { false, false, true }));
            Assert.That(headings, Is.EqualTo(new[] { "Exporting: 2", "Up next: 1" }));
            Assert.That(third.IsFinished, Is.True, "cancelled without ever starting");
            Assert.That(activity.RunningCount, Is.EqualTo(2));
        });
    }

    [Test]
    public void Finished_exports_are_grouped_by_how_long_ago_and_can_be_searched()
    {
        // Two in the afternoon here, wherever here is: days are counted by the local calendar.
        var now = new DateTimeOffset(new DateTime(2026, 10, 8, 14, 0, 0, DateTimeKind.Local));
        ExportJob Finished(string title, double daysAgo) => new(new ExportRecord(
            Video with { Title = title }, new ExportOptions("mp4", "out"), ExportOutcome.Completed, [], null, now.AddDays(-daysAgo)));
        ExportJob[] jobs = [Finished("Old film", 90), Finished("Morning song", 0.2), Finished("Evening song", 1), Finished("Midweek clip", 3)];

        Assert.Multiple(() =>
        {
            Assert.That(
                ActivityViewModel.Group(jobs, "", now).Select(group => $"{group.Title}: {string.Join(", ", group.Select(job => job.Media.Title))}"),
                Is.EqualTo(new[] { "Today: Morning song", "Yesterday: Evening song", "Earlier this week: Midweek clip", "Older: Old film" }));
            Assert.That(ActivityViewModel.Group(jobs, "song .mp4", now).Sum(group => group.Count), Is.EqualTo(2), "every word, in the title or the format");
        });
    }

    [TestCase(50, 10, "50%  ·  about 10 seconds left")]
    [TestCase(25, 60, "25%  ·  about 3 minutes left")]
    [TestCase(2, 10, "2%", Description = "too little done to judge the rest by")]
    [TestCase(40, 1, "40%", Description = "too soon to judge")]
    public void Progress_says_roughly_how_long_is_left(double percent, double seconds, string expected) =>
        Assert.That(ExportJob.DescribeProgress(percent, TimeSpan.FromSeconds(seconds)), Is.EqualTo(expected));

    [Test]
    public void Clearing_finished_exports_keeps_running_ones()
    {
        var settings = new SettingsStore(null);
        settings.Current.History.Add(new ExportRecord(Video, new ExportOptions("mp3", "out") { IncludeVideo = false }, ExportOutcome.Failed, [], "Offline", DateTimeOffset.Now));
        var activity = new ActivityViewModel(new ExportViewModelTests.FakeDownloader(), settings);
        ExportJob running = activity.Start(Video, new ExportOptions("mp3", "out") { IncludeVideo = false });

        activity.ClearFinishedCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(activity.Jobs, Is.EqualTo(new[] { running }));
            Assert.That(settings.Current.History, Is.Empty);
        });
    }
}
