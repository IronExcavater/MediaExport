// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Settings;
using MediaExport.Core.Tooling;
using MediaExport.Tests.Support;
using System.Net;

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
    public async Task Install_writes_and_persists_a_managed_executable()
    {
        _http.Content = [1, 2, 3];

        var installation = await CreateService().InstallAsync("yt-dlp", DownloadUri);

        Assert.Multiple(() =>
        {
            Assert.That(installation.Origin, Is.EqualTo(ToolOrigin.Managed));
            Assert.That(File.ReadAllBytes(installation.Path), Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(_settings.Current.Tools["yt-dlp"].InstallPath, Is.EqualTo(installation.Path));
            Assert.That(_settings.Current.Tools["yt-dlp"].IsInstalled, Is.True);
        });
    }

    [Test]
    public async Task Install_replaces_an_external_active_copy_with_a_managed_copy()
    {
        var external = _temporary.CreateFile(_externalFolder, "yt-dlp.exe", "external");
        _http.Content = "managed"u8.ToArray();
        var service = CreateService();
        Assert.That(service.Find("yt-dlp")!.Origin, Is.EqualTo(ToolOrigin.External));

        var installation = await service.InstallAsync("yt-dlp", DownloadUri);

        Assert.Multiple(() =>
        {
            Assert.That(installation.Origin, Is.EqualTo(ToolOrigin.Managed));
            Assert.That(File.ReadAllText(installation.Path), Is.EqualTo("managed"));
            Assert.That(File.ReadAllText(external), Is.EqualTo("external"));
        });
    }

    [Test]
    public async Task Update_atomically_replaces_a_managed_executable()
    {
        var managed = SeedManaged("old");
        _http.Content = "new"u8.ToArray();

        var installation = await CreateService().UpdateAsync("yt-dlp", DownloadUri);

        Assert.Multiple(() =>
        {
            Assert.That(installation.Path, Is.EqualTo(Path.GetFullPath(managed)));
            Assert.That(File.ReadAllText(managed), Is.EqualTo("new"));
            Assert.That(TemporaryFiles(), Is.Empty);
        });
    }

    [Test]
    public void Failed_update_preserves_the_existing_executable_and_removes_the_temporary_file()
    {
        var managed = SeedManaged("working");
        _http.Failure = new HttpRequestException("offline");

        Assert.ThrowsAsync<HttpRequestException>(
            async () => await CreateService().UpdateAsync("yt-dlp", DownloadUri));

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(managed), Is.EqualTo("working"));
            Assert.That(TemporaryFiles(), Is.Empty);
        });
    }

    [Test]
    public async Task Cancelled_update_preserves_the_existing_executable_and_removes_the_temporary_file()
    {
        var managed = SeedManaged("working");
        _http.Response = async cancellationToken =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        };
        using var cancellation = new CancellationTokenSource();
        var update = CreateService().UpdateAsync("yt-dlp", DownloadUri, cancellation.Token);

        cancellation.Cancel();

        Assert.CatchAsync<OperationCanceledException>(async () => await update);
        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(managed), Is.EqualTo("working"));
            Assert.That(TemporaryFiles(), Is.Empty);
        });
    }

    [Test]
    public void Management_rejects_an_external_copy()
    {
        var external = _temporary.CreateFile(_externalFolder, "yt-dlp.exe");

        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await CreateService().UpdateAsync("yt-dlp", DownloadUri));
        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await CreateService().RemoveAsync("yt-dlp"));
        Assert.Multiple(() =>
        {
            Assert.That(_http.RequestCount, Is.Zero);
            Assert.That(File.Exists(external), Is.True);
        });
    }

    [Test]
    public async Task Remove_clears_managed_state_then_reveals_external_fallback()
    {
        var managed = SeedManaged("managed");
        var external = _temporary.CreateFile(_externalFolder, "yt-dlp.exe", "external");
        var service = CreateService();

        await service.RemoveAsync("yt-dlp");

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(managed), Is.False);
            Assert.That(_settings.Current.Tools["yt-dlp"].InstallPath, Is.Null);
            Assert.That(_settings.Current.Tools["yt-dlp"].IsInstalled, Is.False);
            Assert.That(service.Find("yt-dlp"), Is.EqualTo(new ToolInstallation(Path.GetFullPath(external), ToolOrigin.External)));
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
        var settings = _settings.Current.Tools["yt-dlp"];
        settings.InstallPath = path;
        settings.IsInstalled = true;
        return path;
    }

    private IReadOnlyList<string> TemporaryFiles()
    {
        var folder = Path.Combine(_managedFolder, "yt-dlp");
        return Directory.Exists(folder) ? Directory.GetFiles(folder, "*.tmp") : [];
    }
}
