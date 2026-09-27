// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Exporting;
using MediaExport.Core.Media.Models;
using MediaExport.Core.Settings;

namespace MediaExport.Tests.Settings;

public sealed class SettingsStoreTests
{
    [Test]
    public void Settings_and_history_round_trip_through_the_file()
    {
        string path = Path.Combine(Path.GetTempPath(), $"mediaexport-{Guid.NewGuid():N}.json");
        var media = new MediaSearchResult("id", "https://example.test/media", null, "Result", "Creator", TimeSpan.FromSeconds(90), MediaKind.Audio, ProviderId: "soundcloud");
        try
        {
            var store = new SettingsStore(path);
            store.Current.AudioFormat = "opus";
            store.Current.Window = new WindowPlacement(10, 20, 1200, 800, true);
            store.Current.RecentResults.Add(media);
            store.Current.History.Add(new ExportRecord(
                media,
                new ExportOptions("opus", "out") { IncludeVideo = false, Clips = [new Clip(TimeSpan.Zero, TimeSpan.FromSeconds(30))] },
                ExportOutcome.Completed,
                [@"C:\out\Result.opus"],
                null,
                DateTimeOffset.UnixEpoch));
            store.Save();

            AppSettings loaded = new SettingsStore(path).Current;

            Assert.Multiple(() =>
            {
                Assert.That(loaded.AudioFormat, Is.EqualTo("opus"));
                Assert.That(loaded.Window, Is.EqualTo(store.Current.Window));
                Assert.That(loaded.RecentResults, Is.EqualTo(store.Current.RecentResults));
                Assert.That(loaded.History[0].Options.Clips, Is.EqualTo(new[] { new Clip(TimeSpan.Zero, TimeSpan.FromSeconds(30)) }));
                Assert.That(loaded.History[0].Files, Is.EqualTo(new[] { @"C:\out\Result.opus" }));
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Settings_saved_by_an_earlier_version_still_load()
    {
        string path = Path.Combine(Path.GetTempPath(), $"mediaexport-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """
            {"Theme":"Dark","SubtitleLanguages":"en","RecentResults":[{"Identity":"1","SourceUrl":"https://soundcloud.com/a/b","Title":"Song","Kind":1}]}
            """);
        try
        {
            AppSettings loaded = new SettingsStore(path).Current;

            Assert.Multiple(() =>
            {
                Assert.That(loaded.Theme, Is.EqualTo("Dark"));
                Assert.That(loaded.RecentResults.Single().Title, Is.EqualTo("Song"));
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void A_damaged_file_falls_back_to_defaults()
    {
        string path = Path.Combine(Path.GetTempPath(), $"mediaexport-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ not json");
        try
        {
            Assert.That(new SettingsStore(path).Current.VideoFormat, Is.EqualTo("mp4"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
