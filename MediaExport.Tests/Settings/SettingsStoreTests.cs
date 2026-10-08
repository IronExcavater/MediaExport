// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Settings;

namespace MediaExport.Tests.Settings;

public class SettingsStoreTests
{
    [Test]
    public void SavedSettingsAreReadBack()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "settings.json");
        try
        {
            var store = new SettingsStore(path);
            store.Current.IsPaneOpen = false;
            store.Save();

            Assert.That(new SettingsStore(path).Current.IsPaneOpen, Is.False);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Test]
    public void AnUnreadableFileGivesTheDefaults()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "not json");

            Assert.That(new SettingsStore(path).Current.IsPaneOpen, Is.True);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
