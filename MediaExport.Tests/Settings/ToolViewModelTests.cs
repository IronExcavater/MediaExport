// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Settings;
using MediaExport.Core.Tooling;
using MediaExport.Settings;
using MediaExport.Tests.Support;

namespace MediaExport.Tests.Settings;

public class ToolViewModelTests
{
    private static readonly Uri DownloadUri = new("https://example.test/tool.exe");
    private static readonly ToolInfo Tool = new(
        "tool", "Tool", "tool", "--version",
        new Uri("https://example.test"),
        new Uri("https://example.test/source"),
        DownloadUri);

    private TemporaryDirectory _temporary = null!;
    private SettingsStore _settings = null!;
    private FakeProcessRunner _process = null!;
    private StubHttpMessageHandler _http = null!;
    private HttpClient _client = null!;
    private string _managedFolder = null!;
    private string _externalFolder = null!;

    [SetUp]
    public void SetUp()
    {
        _temporary = new TemporaryDirectory();
        _managedFolder = _temporary.CreateDirectory("managed");
        _externalFolder = _temporary.CreateDirectory("external");
        _settings = new SettingsStore(null);
        _settings.Current.Tools["tool"] = new ToolSettings { Id = "tool", Name = "Tool", Executable = "tool" };
        _process = new FakeProcessRunner { Result = new ProcessResult(0, "tool 2.4.1", "") };
        _http = new StubHttpMessageHandler { Content = "managed"u8.ToArray() };
        _client = new HttpClient(_http);
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _http.Dispose();
        _temporary.Dispose();
    }

    [Test]
    public async Task Refresh_WhenInstallationSourceChanges_ExposesAppropriateActions()
    {
        // Arrange
        var row = CreateRow();

        // Act
        await row.RefreshCommand.ExecuteAsync(null);
        var missingCanInstall = row.CanInstall;
        var external = _temporary.CreateFile(_externalFolder, "tool.exe");
        await row.RefreshCommand.ExecuteAsync(null);
        var externalState = (row.IsExternal, row.CanInstall, row.CanUpdate, row.StatusText);
        var managed = SeedManaged("old");
        await row.RefreshCommand.ExecuteAsync(null);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(missingCanInstall, Is.True);
            Assert.That(externalState, Is.EqualTo((true, true, false, "External")));
            Assert.That(row.Installation, Is.EqualTo(new ToolInstallation(Path.GetFullPath(managed), ToolOrigin.Managed)));
            Assert.That(row.CanInstall, Is.False);
            Assert.That(row.CanUpdate, Is.True);
            Assert.That(row.CanRemove, Is.True);
            Assert.That(File.Exists(external), Is.True);
        });
    }

    [Test]
    public async Task Install_WhenDownloadFails_PreservesExternalCopyAndReportsError()
    {
        // Arrange
        var external = _temporary.CreateFile(_externalFolder, "tool.exe", "external");
        _http.Failure = new HttpRequestException("offline");
        var row = CreateRow();
        await row.RefreshCommand.ExecuteAsync(null);

        // Act
        await row.InstallCommand.ExecuteAsync(null);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(row.IsExternal, Is.True);
            Assert.That(row.Error, Does.Contain("offline"));
            Assert.That(row.IsBusy, Is.False);
            Assert.That(File.ReadAllText(external), Is.EqualTo("external"));
        });
    }

    private ToolViewModel CreateRow() => new(Tool, new ToolchainService(
        _settings,
        new ToolchainOptions(_managedFolder, [_externalFolder]),
        _client,
        _process));

    private string SeedManaged(string contents)
    {
        var path = _temporary.CreateFile(Path.Combine(_managedFolder, "tool"), "tool.exe", contents);
        _settings.Current.Tools["tool"].InstallPath = path;
        _settings.Current.Tools["tool"].IsInstalled = true;
        return path;
    }
}
