// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Settings;
using MediaExport.Core.Tooling;
using MediaExport.Tests.Support;

namespace MediaExport.Tests.Tooling;

public class ToolchainInspectionTests
{
    private static readonly ToolInstallation Installation = new(@"C:\Tools\tool.exe", ToolOrigin.External);

    [TestCase("ffmpeg version 7.1-full_build", "7.1")]
    [TestCase("2026.09.01", "2026.09.01")]
    public async Task Successful_output_returns_the_version(string output, string expected)
    {
        var runner = new FakeProcessRunner { Result = new ProcessResult(0, output, "") };
        var service = CreateService(runner);

        var version = await service.ReadVersionAsync(Installation, "--version");

        Assert.Multiple(() =>
        {
            Assert.That(version, Is.EqualTo(expected));
            Assert.That(runner.Requests, Is.EqualTo(new[] { (Installation.Path, "--version") }));
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

    [TestCase("")]
    [TestCase("version unknown")]
    public void Output_without_a_version_is_an_inspection_failure(string output)
    {
        var runner = new FakeProcessRunner { Result = new ProcessResult(0, output, "") };

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
        runner);
}
