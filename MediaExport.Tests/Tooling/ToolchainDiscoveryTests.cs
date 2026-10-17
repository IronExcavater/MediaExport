// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Settings;
using MediaExport.Core.Tooling;
using MediaExport.Tests.Support;

namespace MediaExport.Tests.Tooling;

public class ToolchainDiscoveryTests
{
    [Test]
    public void Find_WhenCandidatesChange_PrefersManagedThenFallsBackToExternal()
    {
        // Arrange
        using var temporary = new TemporaryDirectory();
        var managedFolder = temporary.CreateDirectory("managed");
        var externalFolder = temporary.CreateDirectory("external");
        var managed = temporary.CreateFile(Path.Combine(managedFolder, "yt-dlp"), "yt-dlp.exe");
        var external = temporary.CreateFile(externalFolder, "yt-dlp.exe");
        var settings = new SettingsStore(null);
        settings.Current.Tools["yt-dlp"].InstallPath = managed;
        var service = new ToolchainService(
            settings,
            new ToolchainOptions(managedFolder, [externalFolder]),
            new HttpClient(),
            new FakeProcessRunner());

        // Act
        var preferred = service.Find("yt-dlp");
        File.Delete(managed);
        var fallback = service.Find("yt-dlp");
        var missing = service.Find("not-installed");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(preferred, Is.EqualTo(new ToolInstallation(Path.GetFullPath(managed), ToolOrigin.Managed)));
            Assert.That(fallback, Is.EqualTo(new ToolInstallation(Path.GetFullPath(external), ToolOrigin.External)));
            Assert.That(missing, Is.Null);
        });
    }
}
