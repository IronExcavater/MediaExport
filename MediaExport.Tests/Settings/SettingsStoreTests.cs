// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using MediaExport.Core.Settings;

namespace MediaExport.Tests.Settings;

public sealed class SettingsStoreTests
{
    [Test]
    public void Settings_round_trip_through_the_file()
    {
        string path = Path.Combine(Path.GetTempPath(), $"mediaexport-{Guid.NewGuid():N}.json");
        try
        {
            var store = new SettingsStore(path);
            store.Current.AudioFormat = "opus";
            store.Current.RecentResults.Add(new MediaSearchResult("id", "https://example.test/media", null, "Result", "Creator", TimeSpan.FromSeconds(90), MediaKind.Audio));
            store.Save();

            AppSettings loaded = new SettingsStore(path).Current;

            Assert.Multiple(() =>
            {
                Assert.That(loaded.AudioFormat, Is.EqualTo("opus"));
                Assert.That(loaded.RecentResults, Is.EqualTo(store.Current.RecentResults));
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
