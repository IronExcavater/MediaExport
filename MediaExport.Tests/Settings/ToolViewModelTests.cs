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
    public async Task Missing_tool_exposes_only_install()
    {
        var row = CreateRow();

        await row.RefreshCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(row.IsMissing, Is.True);
            Assert.That(row.CanInstall, Is.True);
            Assert.That(row.InstallActionText, Is.EqualTo("Install"));
            Assert.That(row.CanUpdate, Is.False);
            Assert.That(row.CanRemove, Is.False);
            Assert.That(row.SourceText, Is.EqualTo("Not found in MediaExport's tools or on PATH"));
        });
    }

    [Test]
    public async Task External_tool_offers_a_managed_copy_without_management_actions()
    {
        var external = _temporary.CreateFile(_externalFolder, "tool.exe");
        var row = CreateRow();

        await row.RefreshCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(row.Installation, Is.EqualTo(new ToolInstallation(Path.GetFullPath(external), ToolOrigin.External)));
            Assert.That(row.IsExternal, Is.True);
            Assert.That(row.CanInstall, Is.True);
            Assert.That(row.InstallActionText, Is.EqualTo("Install managed copy"));
            Assert.That(row.CanUpdate, Is.False);
            Assert.That(row.CanRemove, Is.False);
            Assert.That(row.SourceText, Does.Contain("installed outside MediaExport"));
            Assert.That(row.SourceText, Does.Contain("Updates and removal are handled where it was installed"));
        });
    }

    [Test]
    public async Task Managed_tool_exposes_update_and_remove()
    {
        var managed = SeedManaged("old");
        var row = CreateRow();

        await row.RefreshCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(row.Installation, Is.EqualTo(new ToolInstallation(Path.GetFullPath(managed), ToolOrigin.Managed)));
            Assert.That(row.IsManaged, Is.True);
            Assert.That(row.CanInstall, Is.False);
            Assert.That(row.CanUpdate, Is.True);
            Assert.That(row.CanRemove, Is.True);
            Assert.That(row.SourceText, Does.StartWith("Installed by MediaExport"));
        });
    }

    [Test]
    public async Task Refresh_reports_an_inspection_failure_instead_of_a_healthy_tool()
    {
        _temporary.CreateFile(_externalFolder, "tool.exe");
        _process.Failure = new IOException("cannot start");
        var row = CreateRow();

        await row.RefreshCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(row.HasError, Is.True);
            Assert.That(row.IsHealthy, Is.False);
            Assert.That(row.Error, Does.Contain("Couldn't inspect Tool"));
        });
    }

    [Test]
    public async Task Failed_refresh_clears_the_previous_version()
    {
        _temporary.CreateFile(_externalFolder, "tool.exe");
        var row = CreateRow();
        await row.RefreshCommand.ExecuteAsync(null);
        _process.Failure = new IOException("cannot start");

        await row.RefreshCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(row.Version, Is.Null);
            Assert.That(row.VersionText, Is.EqualTo("Installed"));
            Assert.That(row.HasError, Is.True);
        });
    }

    [Test]
    public async Task Install_on_external_selects_the_new_managed_copy()
    {
        var external = _temporary.CreateFile(_externalFolder, "tool.exe", "external");
        var row = CreateRow();
        await row.RefreshCommand.ExecuteAsync(null);

        await row.InstallCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(row.IsManaged, Is.True);
            Assert.That(File.ReadAllText(row.Installation!.Path), Is.EqualTo("managed"));
            Assert.That(File.ReadAllText(external), Is.EqualTo("external"));
            Assert.That(_http.RequestCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Update_and_remove_refresh_to_the_resulting_installation()
    {
        SeedManaged("old");
        var external = _temporary.CreateFile(_externalFolder, "tool.exe", "external");
        var row = CreateRow();
        await row.RefreshCommand.ExecuteAsync(null);

        await row.UpdateCommand.ExecuteAsync(null);
        var updatedContents = File.ReadAllText(row.Installation!.Path);
        await row.RemoveCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(updatedContents, Is.EqualTo("managed"));
            Assert.That(_http.RequestCount, Is.EqualTo(1));
            Assert.That(row.Installation, Is.EqualTo(new ToolInstallation(Path.GetFullPath(external), ToolOrigin.External)));
        });
    }

    [Test]
    public async Task Operation_failures_stay_on_the_row_and_keep_the_previous_state()
    {
        _temporary.CreateFile(_externalFolder, "tool.exe");
        _http.Failure = new HttpRequestException("offline");
        var row = CreateRow();
        await row.RefreshCommand.ExecuteAsync(null);

        await row.InstallCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(row.IsExternal, Is.True);
            Assert.That(row.Error, Does.Contain("offline"));
            Assert.That(row.IsBusy, Is.False);
        });
    }

    [Test]
    public async Task Removal_failure_keeps_the_managed_installation_and_reports_the_error()
    {
        var managed = SeedManaged("working");
        var row = CreateRow();
        await row.RefreshCommand.ExecuteAsync(null);
        await using var locked = new FileStream(managed, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        await row.RemoveCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(row.IsManaged, Is.True);
            Assert.That(row.Error, Does.Contain("Couldn't remove Tool"));
            Assert.That(File.Exists(managed), Is.True);
        });
    }

    [Test]
    public async Task A_running_operation_disables_every_command_and_cancellation_is_not_an_error()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _http.Response = async cancellationToken =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable");
        };
        var row = CreateRow();
        await row.RefreshCommand.ExecuteAsync(null);

        var install = row.InstallCommand.ExecuteAsync(null);
        await started.Task;

        Assert.Multiple(() =>
        {
            Assert.That(row.RefreshCommand.CanExecute(null), Is.False);
            Assert.That(row.InstallCommand.CanExecute(null), Is.False);
            Assert.That(row.UpdateCommand.CanExecute(null), Is.False);
            Assert.That(row.RemoveCommand.CanExecute(null), Is.False);
        });

        row.InstallCommand.Cancel();
        await install;
        Assert.Multiple(() =>
        {
            Assert.That(row.Error, Is.Null);
            Assert.That(row.IsBusy, Is.False);
            Assert.That(row.RefreshCommand.CanExecute(null), Is.True);
        });
    }

    private ToolViewModel CreateRow() => new(Tool, CreateService());

    private ToolchainService CreateService() => new(
        _settings,
        new ToolchainOptions(_managedFolder, [_externalFolder]),
        _client,
        _process);

    private string SeedManaged(string contents)
    {
        var path = _temporary.CreateFile(Path.Combine(_managedFolder, "tool"), "tool.exe", contents);
        _settings.Current.Tools["tool"].InstallPath = path;
        _settings.Current.Tools["tool"].IsInstalled = true;
        return path;
    }
}
