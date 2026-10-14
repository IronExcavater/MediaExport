// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Settings;
using MediaExport.Core.Tooling;
using MediaExport.Tests.Support;

namespace MediaExport.Tests.Tooling;

public class ToolchainDiscoveryTests
{
    private TemporaryDirectory _temporary = null!;
    private SettingsStore _settings = null!;
    private string _managedFolder = null!;
    private string _externalFolder = null!;

    [SetUp]
    public void SetUp()
    {
        _temporary = new TemporaryDirectory();
        _managedFolder = _temporary.CreateDirectory("managed");
        _externalFolder = _temporary.CreateDirectory("external");
        _settings = new SettingsStore(null);
    }

    [TearDown]
    public void TearDown() => _temporary.Dispose();

    [Test]
    public void Find_prefers_the_saved_managed_executable()
    {
        var managed = _temporary.CreateFile(Path.Combine(_managedFolder, "yt-dlp"), "yt-dlp.exe");
        _temporary.CreateFile(_externalFolder, "yt-dlp.exe");
        _settings.Current.Tools["yt-dlp"].InstallPath = managed;

        var installation = CreateService().Find("yt-dlp");

        Assert.That(installation, Is.EqualTo(new ToolInstallation(Path.GetFullPath(managed), ToolOrigin.Managed)));
    }

    [Test]
    public void Find_uses_the_first_external_executable_when_managed_is_missing()
    {
        var first = _temporary.CreateFile(_externalFolder, "yt-dlp.exe");
        var secondFolder = _temporary.CreateDirectory("second");
        _temporary.CreateFile(secondFolder, "yt-dlp.exe");

        var installation = CreateService(_externalFolder, secondFolder).Find("yt-dlp");

        Assert.That(installation, Is.EqualTo(new ToolInstallation(Path.GetFullPath(first), ToolOrigin.External)));
    }

    [Test]
    public void Find_falls_back_when_the_saved_managed_file_was_deleted()
    {
        _settings.Current.Tools["yt-dlp"].InstallPath = Path.Combine(_managedFolder, "yt-dlp", "yt-dlp.exe");
        var external = _temporary.CreateFile(_externalFolder, "yt-dlp.exe");

        var installation = CreateService().Find("yt-dlp");

        Assert.That(installation, Is.EqualTo(new ToolInstallation(Path.GetFullPath(external), ToolOrigin.External)));
    }

    [Test]
    public void Find_returns_null_when_no_candidate_exists()
    {
        Assert.That(CreateService().Find("not-installed"), Is.Null);
    }

    [Test]
    public void A_prefixed_sibling_directory_is_external()
    {
        var sibling = _temporary.CreateDirectory("managed-elsewhere");
        var executable = _temporary.CreateFile(sibling, "yt-dlp.exe");
        _settings.Current.Tools["yt-dlp"].InstallPath = executable;

        var installation = CreateService(sibling).Find("yt-dlp");

        Assert.That(installation, Is.EqualTo(new ToolInstallation(Path.GetFullPath(executable), ToolOrigin.External)));
    }

    private ToolchainService CreateService(params string[]? searchPath) => new(
        _settings,
        new ToolchainOptions(_managedFolder, searchPath is { Length: > 0 } ? searchPath : [_externalFolder]),
        new HttpClient(),
        new FakeProcessRunner());
}
