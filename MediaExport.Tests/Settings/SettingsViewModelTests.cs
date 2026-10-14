// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Settings;
using MediaExport.Core.Tooling;
using MediaExport.Settings;
using MediaExport.Tests.Support;

namespace MediaExport.Tests.Settings;

public class SettingsViewModelTests
{
    [Test]
    public async Task Auto_update_runs_once_for_managed_downloadable_tools()
    {
        using var temporary = new TemporaryDirectory();
        var managedFolder = temporary.CreateDirectory("managed");
        var managed = temporary.CreateFile(Path.Combine(managedFolder, "yt-dlp"), "yt-dlp.exe", "old");
        var settings = new SettingsStore(null);
        settings.Current.Tools["yt-dlp"].InstallPath = managed;
        settings.Current.Tools["yt-dlp"].IsInstalled = true;
        settings.Current.Tools["yt-dlp"].AutoUpdate = true;
        using var handler = new StubHttpMessageHandler { Content = "new"u8.ToArray() };
        using var client = new HttpClient(handler);
        var tools = new ToolchainService(
            settings,
            new ToolchainOptions(managedFolder, []),
            client,
            new FakeProcessRunner());
        var viewModel = new SettingsViewModel(tools, settings, new FakeThemeService(), new FakeFolderLauncher());

        await viewModel.RefreshToolsAsync();
        await viewModel.RefreshToolsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(handler.RequestCount, Is.EqualTo(1));
            Assert.That(File.ReadAllText(managed), Is.EqualTo("new"));
        });
    }

    [Test]
    public async Task Auto_update_never_changes_an_external_tool()
    {
        using var temporary = new TemporaryDirectory();
        var managedFolder = temporary.CreateDirectory("managed");
        var externalFolder = temporary.CreateDirectory("external");
        var external = temporary.CreateFile(externalFolder, "yt-dlp.exe", "external");
        var settings = new SettingsStore(null);
        settings.Current.Tools["yt-dlp"].AutoUpdate = true;
        using var handler = new StubHttpMessageHandler { Content = "new"u8.ToArray() };
        using var client = new HttpClient(handler);
        var tools = new ToolchainService(
            settings,
            new ToolchainOptions(managedFolder, [externalFolder]),
            client,
            new FakeProcessRunner());
        var viewModel = new SettingsViewModel(tools, settings, new FakeThemeService(), new FakeFolderLauncher());

        await viewModel.RefreshToolsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(handler.RequestCount, Is.Zero);
            Assert.That(File.ReadAllText(external), Is.EqualTo("external"));
        });
    }

    private sealed class FakeThemeService : IThemeService
    {
        public AppTheme CurrentTheme { get; private set; }
        public void Apply(AppTheme theme) => CurrentTheme = theme;
    }

    private sealed class FakeFolderLauncher : IFolderLauncher
    {
        public void Open(string folder)
        {
        }
    }
}
