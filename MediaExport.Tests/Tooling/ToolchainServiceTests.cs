// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Tooling;
using MediaExport.Settings;
using System.IO.Compression;
using System.Net;
using System.Text;

namespace MediaExport.Tests.Tooling;

public sealed class ToolchainServiceTests
{
    private string _folder = null!;

    [SetUp]
    public void CreateFolder() => _folder = Directory.CreateTempSubdirectory("mediaexport-tools-").FullName;

    [TearDown]
    public void DeleteFolder() => Directory.Delete(_folder, true);

    [Test]
    public async Task The_latest_release_is_the_newest_published_one_with_a_windows_build()
    {
        ToolchainService service = Create(new FakeGitHub());

        ToolRelease? latest = await service.GetLatestAsync(ToolchainService.FFmpeg);

        Assert.That(latest?.Version, Is.EqualTo("9.0.2"), "drafts, pre-releases and releases without a build are skipped");
    }

    [Test]
    public async Task An_installed_copy_is_used_ahead_of_one_on_PATH_and_can_be_removed()
    {
        var onPath = new Dictionary<string, string> { ["ffmpeg"] = @"C:\path\ffmpeg.exe", ["ffprobe"] = @"C:\path\ffprobe.exe" };
        ToolchainService service = Create(new FakeGitHub(), onPath);

        await service.InstallAsync(ToolchainService.FFmpeg);
        ToolStatus managed = await service.CheckAsync(ToolchainService.FFmpeg);
        service.Remove(ToolchainService.FFmpeg);
        ToolStatus system = await service.CheckAsync(ToolchainService.FFmpeg);

        Assert.Multiple(() =>
        {
            Assert.That(managed.IsManaged, Is.True);
            Assert.That(managed.Version, Is.EqualTo("9.0.2"));
            Assert.That(File.Exists(Path.Combine(_folder, "ffmpeg", "ffprobe.exe")), Is.False, "removed with the rest");
            Assert.That(system.Path, Is.EqualTo(@"C:\path\ffmpeg.exe"));
            Assert.That(system.IsManaged, Is.False);
        });
    }

    [Test]
    public void Missing_required_tools_are_reported()
    {
        ToolchainService service = Create(new FakeGitHub(), new Dictionary<string, string> { ["yt-dlp"] = @"C:\yt-dlp.exe" });

        Assert.That(service.FindMissingRequiredTools(), Is.EqualTo(new[] { ToolchainService.FFmpeg }));
    }

    [TestCase("ffmpeg version 7.1-full_build-www.gyan.dev Copyright (c) 2000-2024", "7.1")]
    [TestCase("deno 2.4.1 (stable, release, x86_64-pc-windows-msvc)", "2.4.1")]
    [TestCase("2026.08.19", "2026.08.19")]
    public void ParseVersion_finds_the_version_number(string output, string expected) =>
        Assert.That(ToolchainService.ParseVersion(output), Is.EqualTo(expected));

    [TestCase("2026.09.20", "2026.08.19", true)]
    [TestCase("9.0.2", "9.0.10", false)]
    [TestCase("2.9.7", "2.9.7", false)]
    [TestCase("8.1", "8.0.1", true)]
    public void Newer_versions_compare_part_by_part(string candidate, string current, bool expected) =>
        Assert.That(ToolViewModel.IsNewer(candidate, current), Is.EqualTo(expected));

    [TestCase(@"C:\Tools\FFMPEG.EXE", @"C:\Tools\FFMPEG.exe")]
    [TestCase(@"C:\Mixed Case\yt-dlp.Cmd", @"C:\Mixed Case\yt-dlp.cmd")]
    [TestCase(@"C:\Tools\deno", @"C:\Tools\deno")]
    public void Displayed_tool_paths_preserve_path_case_and_lowercase_only_the_extension(string path, string expected) =>
        Assert.That(ToolViewModel.DisplayPath(path), Is.EqualTo(expected));

    [Test]
    public async Task Install_action_stays_hidden_until_status_and_release_checks_finish()
    {
        var releases = new PausedResponse();
        ToolchainService service = Create(releases);
        var tool = new ToolViewModel(ToolchainService.Deno, service, () => { });

        Task refresh = tool.RefreshAsync();
        await releases.Requested.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Multiple(() =>
        {
            Assert.That(tool.Status, Is.Null, "the card remains in its quiet checking state until discovery finishes");
            Assert.That(tool.VersionText, Is.Empty, "loading belongs in the status icon, not the text");
            Assert.That(tool.HasPrimaryAction, Is.False, "the release lookup is still in progress");
        });

        releases.Complete("""
            [{"tag_name":"v2.5.0","draft":false,"prerelease":false,"published_at":"2026-10-01T00:00:00Z","assets":[{"name":"deno-x86_64-pc-windows-msvc.zip","browser_download_url":"https://example.test/deno.zip","size":1}]}]
            """);
        await refresh;

        Assert.Multiple(() =>
        {
            Assert.That(tool.HasPrimaryAction, Is.True);
            Assert.That(tool.PrimaryAction, Is.EqualTo("Install"));
        });
    }

    [Test]
    public async Task A_failed_install_keeps_the_install_action_available_for_retry()
    {
        ToolchainService service = Create(new FakeGitHub(failDownload: true));
        var tool = new ToolViewModel(ToolchainService.FFmpeg, service, () => { });

        await tool.RefreshAsync();
        await tool.InstallCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(tool.Error, Is.Not.Null);
            Assert.That(tool.PrimaryAction, Is.EqualTo("Install"));
            Assert.That(tool.HasPrimaryAction, Is.True, "the failed action can be retried without leaving Settings");
        });
    }

    [Test]
    public async Task A_failed_status_check_leaves_the_loading_state_and_shows_the_error()
    {
        var onPath = new Dictionary<string, string> { ["ffmpeg"] = @"C:\Tools\FFmpeg.EXE", ["ffprobe"] = @"C:\Tools\FFprobe.EXE" };
        var service = new ToolchainService(
            _folder,
            new HttpClient(new FakeGitHub()),
            (_, _, _) => Task.FromException<ProcessResult>(new InvalidOperationException("Version check failed")),
            command => onPath.GetValueOrDefault(command));
        var tool = new ToolViewModel(ToolchainService.FFmpeg, service, () => { });

        await tool.RefreshAsync();

        Assert.Multiple(() =>
        {
            Assert.That(tool.IsChecking, Is.False);
            Assert.That(tool.HasError, Is.True);
        });
    }

    [Test]
    public async Task A_failed_post_action_status_check_always_releases_the_command_for_retry()
    {
        int checks = 0;
        var onPath = new Dictionary<string, string> { ["ffmpeg"] = @"C:\Tools\FFmpeg.EXE", ["ffprobe"] = @"C:\Tools\FFprobe.EXE" };
        var service = new ToolchainService(
            _folder,
            new HttpClient(new FakeGitHub(failDownload: true)),
            (_, _, _) => ++checks == 1
                ? Task.FromResult(new ProcessResult(0, "8.0.1", string.Empty))
                : Task.FromException<ProcessResult>(new InvalidOperationException("Follow-up check failed")),
            command => onPath.GetValueOrDefault(command));
        var tool = new ToolViewModel(ToolchainService.FFmpeg, service, () => { });

        await tool.RefreshAsync();
        Assert.DoesNotThrowAsync(async () => await tool.InstallCommand.ExecuteAsync(null));

        Assert.Multiple(() =>
        {
            Assert.That(tool.IsWorking, Is.False);
            Assert.That(tool.InstallCommand.CanExecute(null), Is.True);
            Assert.That(tool.Error, Does.Not.Contain("Follow-up check failed"), "the original action failure remains the useful error");
        });
    }

    private ToolchainService Create(HttpMessageHandler github, Dictionary<string, string>? onPath = null) =>
        new(_folder, new HttpClient(github), (_, _, _) => Task.FromResult(new ProcessResult(0, "8.0.1", string.Empty)), command => onPath?.GetValueOrDefault(command));

    private sealed class FakeGitHub : HttpMessageHandler
    {
        private readonly bool _failDownload;

        public FakeGitHub(bool failDownload = false) => _failDownload = failDownload;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Host == "api.github.com")
            {
                const string releases = """
                    [
                      {"tag_name":"9.1","draft":true,"prerelease":false,"published_at":"2026-09-01T00:00:00Z","assets":[{"name":"ffmpeg-9.1-essentials_build.zip","browser_download_url":"https://example.test/9.1.zip","size":1}]},
                      {"tag_name":"9.0.3","draft":false,"prerelease":true,"published_at":"2026-08-20T00:00:00Z","assets":[{"name":"ffmpeg-9.0.3-essentials_build.zip","browser_download_url":"https://example.test/9.0.3.zip","size":1}]},
                      {"tag_name":"9.0.2","draft":false,"prerelease":false,"published_at":"2026-08-01T00:00:00Z","assets":[{"name":"ffmpeg-9.0.2-essentials_build.zip","browser_download_url":"https://example.test/9.0.2.zip","size":1}]},
                      {"tag_name":"9.0.1","draft":false,"prerelease":false,"published_at":"2026-07-01T00:00:00Z","assets":[{"name":"ffmpeg-9.0.1-full_build.7z","browser_download_url":"https://example.test/9.0.1.7z","size":1}]}
                    ]
                    """;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(releases, Encoding.UTF8, "application/json") });
            }

            if (_failDownload)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));

            // The archive nests the executables in a versioned folder, as the real one does.
            var archive = new MemoryStream();
            using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (string name in new[] { "ffmpeg-9.0.2-essentials_build/bin/ffmpeg.exe", "ffmpeg-9.0.2-essentials_build/bin/ffprobe.exe", "ffmpeg-9.0.2-essentials_build/README.txt" })
                {
                    using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                    writer.Write("binary");
                }
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive.ToArray()) });
        }
    }

    private sealed class PausedResponse : HttpMessageHandler
    {
        private readonly TaskCompletionSource<string> _response = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Requested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Complete(string json) => _response.SetResult(json);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requested.TrySetResult();
            string json = await _response.Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
