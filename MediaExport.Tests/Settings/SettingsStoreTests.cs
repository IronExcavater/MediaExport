// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Settings;

namespace MediaExport.Tests.Settings;

public class SettingsStoreTests
{
    [Test]
    public void Load_WhenFileIsSavedOrInvalid_RestoresValuesOrDefaults()
    {
        // Arrange
        var folder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var savedPath = Path.Combine(folder, "saved.json");
        var invalidPath = Path.Combine(folder, "invalid.json");
        Directory.CreateDirectory(folder);

        try
        {
            var store = new SettingsStore(savedPath);
            store.Current.IsPaneOpen = false;
            store.Save();
            File.WriteAllText(invalidPath, "not json");

            // Act
            var saved = new SettingsStore(savedPath).Current;
            var defaults = new SettingsStore(invalidPath).Current;

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(saved.IsPaneOpen, Is.False);
                Assert.That(defaults.IsPaneOpen, Is.True);
            });
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
