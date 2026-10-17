// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Settings;
using MediaExport.Core.Tooling;
using MediaExport.Tests.Support;
using System.IO.Compression;

namespace MediaExport.Tests.Tooling;

public class ToolchainManagementTests
{
    private static readonly Uri DownloadUri = new("https://example.test/yt-dlp.exe");
    private TemporaryDirectory _temporary = null!;
    private SettingsStore _settings = null!;
    private StubHttpMessageHandler _http = null!;
    private string _managedFolder = null!;
    private string _externalFolder = null!;

    [SetUp]
    public void SetUp()
    {
        _temporary = new TemporaryDirectory();
        _managedFolder = _temporary.CreateDirectory("managed");
        _externalFolder = _temporary.CreateDirectory("external");
        _settings = new SettingsStore(null);
        _http = new StubHttpMessageHandler();
    }

    [TearDown]
    public void TearDown()
    {
        _http.Dispose();
        _temporary.Dispose();
    }

    [Test]
    public async Task Manage_WhenInstallUpdateAndRemoveSucceed_PersistsEachState()
    {
        // Arrange
        _http.Content = "first"u8.ToArray();
        var external = _temporary.CreateFile(_externalFolder, "yt-dlp.exe", "external");
        var service = CreateService();

        // Act
        var installed = await service.InstallAsync("yt-dlp", DownloadUri);
        _http.Content = "updated"u8.ToArray();
        var updated = await service.UpdateAsync("yt-dlp", DownloadUri);
        await service.RemoveAsync("yt-dlp");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(installed.Origin, Is.EqualTo(ToolOrigin.Managed));
            Assert.That(updated.Path, Is.EqualTo(installed.Path));
            Assert.That(File.Exists(installed.Path), Is.False);
            Assert.That(_settings.Current.Tools["yt-dlp"].IsInstalled, Is.False);
            Assert.That(service.Find("yt-dlp"), Is.EqualTo(new ToolInstallation(Path.GetFullPath(external), ToolOrigin.External)));
        });
    }

    [Test]
    public async Task Manage_WhenInstallingZip_ReturnsExecutablePathAndRemovesWholeToolDirectory()
    {
        // Arrange
        using var archiveStream = new MemoryStream();
        using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("ffmpeg/bin/ffmpeg.exe");
            await using var entryStream = entry.Open();
            await entryStream.WriteAsync("ffmpeg"u8.ToArray());
        }

        _http.Content = archiveStream.ToArray();
        var service = CreateService();
        var toolDirectory = Path.Combine(_managedFolder, "ffmpeg");

        // Act
        var installed = await service.InstallAsync(
            "ffmpeg",
            new Uri("https://example.test/ffmpeg.zip"));

        // Assert installation path before removing the archive.
        Assert.Multiple(() =>
        {
            Assert.That(installed.Path, Is.EqualTo(_settings.Current.Tools["ffmpeg"].InstallPath));
            Assert.That(File.Exists(installed.Path), Is.True);
        });

        // Act
        await service.RemoveAsync("ffmpeg");

        // Assert
        Assert.That(Directory.Exists(toolDirectory), Is.False);
    }

    [Test]
    public void Update_WhenDownloadFails_PreservesManagedExecutableAndCleansTemporaryFile()
    {
        // Arrange
        var managed = SeedManaged("working");
        _http.Failure = new HttpRequestException("offline");
        var service = CreateService();

        // Act
        var error = Assert.ThrowsAsync<HttpRequestException>(
            async () => await service.UpdateAsync("yt-dlp", DownloadUri));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(error!.Message, Does.Contain("offline"));
            Assert.That(File.ReadAllText(managed), Is.EqualTo("working"));
            Assert.That(TemporaryFiles(), Is.Empty);
        });
    }

    private ToolchainService CreateService() => new(
        _settings,
        new ToolchainOptions(_managedFolder, [_externalFolder]),
        new HttpClient(_http),
        new FakeProcessRunner());

    private string SeedManaged(string contents)
    {
        var path = _temporary.CreateFile(Path.Combine(_managedFolder, "yt-dlp"), "yt-dlp.exe", contents);
        _settings.Current.Tools["yt-dlp"].InstallPath = path;
        _settings.Current.Tools["yt-dlp"].IsInstalled = true;
        return path;
    }

    private IReadOnlyList<string> TemporaryFiles()
    {
        var folder = Path.Combine(_managedFolder, "yt-dlp");
        return Directory.Exists(folder) ? Directory.GetFiles(folder, "*.tmp") : [];
    }
}
