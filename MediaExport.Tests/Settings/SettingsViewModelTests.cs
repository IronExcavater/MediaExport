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
        var viewModel = CreateViewModel(tools, settings);

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
        var viewModel = CreateViewModel(tools, settings);

        await viewModel.RefreshToolsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(handler.RequestCount, Is.Zero);
            Assert.That(File.ReadAllText(external), Is.EqualTo("external"));
        });
    }

    [Test]
    public void About_information_comes_from_the_metadata_provider()
    {
        using var temporary = new TemporaryDirectory();
        var settings = new SettingsStore(null);
        using var client = new HttpClient();
        var tools = new ToolchainService(
            settings,
            new ToolchainOptions(temporary.CreateDirectory("managed"), []),
            client,
            new FakeProcessRunner());
        var metadata = new AppMetadata(
            "Test Exporter",
            "2.4.1",
            "By Test Contributors",
            new Uri("https://example.test/source"),
            new Uri("https://example.test/issues/new"),
            new Uri("https://example.test/licence"));

        var viewModel = new SettingsViewModel(
            tools,
            settings,
            new FakeThemeService(),
            new FakeFolderLauncher(),
            new FakeMetadataProvider(metadata));

        Assert.That(viewModel.App, Is.EqualTo(metadata));
    }

    private static SettingsViewModel CreateViewModel(ToolchainService tools, SettingsStore settings) =>
        new(tools, settings, new FakeThemeService(), new FakeFolderLauncher(), new FakeMetadataProvider());

    private sealed class FakeMetadataProvider(AppMetadata? metadata = null) : IAppMetadataProvider
    {
        public AppMetadata Get() => metadata ?? new AppMetadata(
            "MediaExport",
            "1.0.0",
            "By contributors",
            new Uri("https://example.test/source"),
            new Uri("https://example.test/issues/new"),
            new Uri("https://example.test/licence"));
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
