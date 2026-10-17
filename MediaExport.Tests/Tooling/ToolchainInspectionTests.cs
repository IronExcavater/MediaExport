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
    public async Task ReadVersion_WhenProcessSucceedsOrFails_ReturnsVersionOrUsefulError()
    {
        // Arrange
        var runner = new FakeProcessRunner { Result = new ProcessResult(0, "ffmpeg version 7.1-full_build", "") };
        var service = CreateService(runner);

        // Act
        var version = await service.ReadVersionAsync(Installation, "--version");
        runner.Result = new ProcessResult(2, "", "first detail\r\nactual failure\r\n");
        var error = Assert.ThrowsAsync<ToolInspectionException>(
            async () => await service.ReadVersionAsync(Installation, "--version"));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(version, Is.EqualTo("7.1"));
            Assert.That(error!.Message, Does.Contain("actual failure"));
            Assert.That(runner.Requests, Has.All.EqualTo((Installation.Path, "--version")));
        });
    }

    private static ToolchainService CreateService(IProcessRunner runner) => new(
        new SettingsStore(null),
        new ToolchainOptions(Path.GetTempPath(), []),
        new HttpClient(),
        runner);
}
