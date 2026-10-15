// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Settings;
using MediaExport.Core.Tooling;
using MediaExport.Tests.Support;

namespace MediaExport.Tests.Tooling;

public class ToolchainInspectionTests
{
    private static readonly ToolInstallation Installation = new(@"C:\Tools\tool.exe", ToolOrigin.External);

    [Test]
    public async Task Successful_output_returns_the_version()
    {
        var runner = new FakeProcessRunner { Result = new ProcessResult(0, "ffmpeg version 7.1-full_build", "") };

        var ffmpeg = await CreateService(runner).ReadVersionAsync(Installation, "--version");
        runner.Result = new ProcessResult(0, "2026.09.01", "");
        var dated = await CreateService(runner).ReadVersionAsync(Installation, "--version");

        Assert.Multiple(() =>
        {
            Assert.That(ffmpeg, Is.EqualTo("7.1"));
            Assert.That(dated, Is.EqualTo("2026.09.01"));
            Assert.That(runner.Requests, Has.All.EqualTo((Installation.Path, "--version")));
        });
    }

    [Test]
    public void A_start_failure_identifies_the_executable()
    {
        var runner = new FakeProcessRunner { Failure = new IOException("cannot start") };

        var error = Assert.ThrowsAsync<ToolInspectionException>(
            async () => await CreateService(runner).ReadVersionAsync(Installation, "--version"));

        Assert.That(error!.Message, Does.Contain(Installation.Path));
    }

    [Test]
    public void A_nonzero_exit_reports_the_last_error_line()
    {
        var runner = new FakeProcessRunner
        {
            Result = new ProcessResult(2, "", "first detail\r\nactual failure\r\n")
        };

        var error = Assert.ThrowsAsync<ToolInspectionException>(
            async () => await CreateService(runner).ReadVersionAsync(Installation, "--version"));

        Assert.That(error!.Message, Does.Contain("actual failure"));
    }

    [Test]
    public void Output_without_a_version_is_an_inspection_failure()
    {
        var runner = new FakeProcessRunner { Result = new ProcessResult(0, "", "") };
        Assert.ThrowsAsync<ToolInspectionException>(
            async () => await CreateService(runner).ReadVersionAsync(Installation, "--version"));
        runner.Result = new ProcessResult(0, "version unknown", "");

        Assert.ThrowsAsync<ToolInspectionException>(
            async () => await CreateService(runner).ReadVersionAsync(Installation, "--version"));
    }

    [Test]
    public void Cancellation_remains_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var runner = new FakeProcessRunner
        {
            Failure = new OperationCanceledException(cancellation.Token)
        };

        Assert.ThrowsAsync<OperationCanceledException>(
            async () => await CreateService(runner).ReadVersionAsync(Installation, "--version", cancellation.Token));
    }

    private static ToolchainService CreateService(IProcessRunner runner) => new(
        new SettingsStore(null),
        new ToolchainOptions(Path.GetTempPath(), []),
        new HttpClient(),
        runner);
}
