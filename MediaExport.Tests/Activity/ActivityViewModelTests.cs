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
            Assert.That(restored.Jobs[0].Summary, Is.EqualTo("MP4 720p"));
        });
    }

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
