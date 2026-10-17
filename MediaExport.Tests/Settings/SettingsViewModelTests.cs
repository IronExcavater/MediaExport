// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Activity;
using MediaExport.Core.Exporting;
using MediaExport.Core.Media.Providers;
using MediaExport.Core.Settings;
using MediaExport.Core.Tooling;
using MediaExport.Export;
using MediaExport.Search;
using MediaExport.Settings;
using MediaExport.Tests.Support;

namespace MediaExport.Tests.Settings;

public class SettingsViewModelTests
{
    [Test]
    public async Task RefreshTools_WhenDependenciesAppear_UpdatesWarningAndKeepsMetadata()
    {
        // Arrange
        using var temporary = new TemporaryDirectory();
        var managedFolder = temporary.CreateDirectory("managed");
        var externalFolder = temporary.CreateDirectory("external");
        var settings = new SettingsStore(null);
        using var client = new HttpClient();
        var tools = new ToolchainService(settings, new ToolchainOptions(managedFolder, [externalFolder]), client, new FakeProcessRunner());
        var metadata = new AppMetadata(
            "Test Exporter", "2.4.1", "By Test Contributors",
            new Uri("https://example.test/source"),
            new Uri("https://example.test/issues/new"),
            new Uri("https://example.test/licence"));
        var viewModel = CreateViewModel(tools, settings, metadata);

        // Act
        await viewModel.RefreshToolsAsync();
        var missingCount = viewModel.MissingToolCount;
        temporary.CreateFile(externalFolder, "yt-dlp.exe");
        temporary.CreateFile(externalFolder, "ffmpeg.exe");
        temporary.CreateFile(externalFolder, "deno.exe");
        await viewModel.RefreshToolsAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(missingCount, Is.EqualTo(3));
            Assert.That(viewModel.HasMissingTools, Is.False);
            Assert.That(viewModel.MissingToolCount, Is.Zero);
            Assert.That(viewModel.App, Is.EqualTo(metadata));
        });
    }

    [Test]
    public async Task RefreshTools_WhenAutoUpdateEnabled_UpdatesManagedCopyOnlyOnce()
    {
        // Arrange
        using var temporary = new TemporaryDirectory();
        var managedFolder = temporary.CreateDirectory("managed");
        var externalFolder = temporary.CreateDirectory("external");
        var managed = temporary.CreateFile(Path.Combine(managedFolder, "yt-dlp"), "yt-dlp.exe", "old");
        var external = temporary.CreateFile(externalFolder, "ffmpeg.exe", "external");
        var settings = new SettingsStore(null);
        settings.Current.Tools["yt-dlp"].InstallPath = managed;
        settings.Current.Tools["yt-dlp"].IsInstalled = true;
        settings.Current.Tools["yt-dlp"].AutoUpdate = true;
        settings.Current.Tools["ffmpeg"].AutoUpdate = true;
        using var handler = new StubHttpMessageHandler { Content = "new"u8.ToArray() };
        using var client = new HttpClient(handler);
        var tools = new ToolchainService(settings, new ToolchainOptions(managedFolder, [externalFolder]), client, new FakeProcessRunner());
        var viewModel = CreateViewModel(tools, settings);

        // Act
        await viewModel.RefreshToolsAsync();
        await viewModel.RefreshToolsAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(handler.RequestCount, Is.EqualTo(1));
            Assert.That(File.ReadAllText(managed), Is.EqualTo("new"));
            Assert.That(File.ReadAllText(external), Is.EqualTo("external"));
        });
    }

    private static SettingsViewModel CreateViewModel(
        ToolchainService tools,
        SettingsStore settings,
        AppMetadata? metadata = null)
    {
        var downloader = new UnusedDownloader();
        var activity = new ActivityViewModel(downloader, settings);
        var search = new SearchViewModel([], new SearchCache(null));
        var export = new ExportViewModel(activity, settings, [], downloader);
        return new SettingsViewModel(
            tools, settings, new FakeThemeService(), search, export, activity,
            new FakeFolderLauncher(), new FakeMetadataProvider(metadata));
    }

    private sealed class UnusedDownloader : IMediaDownloader
    {
        public Task<IReadOnlyList<string>> DownloadAsync(
            string sourceUrl,
            ExportOptions options,
            IProgress<ExportProgress>? progress = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeMetadataProvider(AppMetadata? metadata = null) : IAppMetadataProvider
    {
        public AppMetadata Get() => metadata ?? new AppMetadata(
            "MediaExport", "1.0.0", "By contributors",
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
