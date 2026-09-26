// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Tooling;

namespace MediaExport.Tests.Tooling;

public sealed class ToolchainServiceTests
{
    [Test]
    public async Task Missing_tools_can_be_installed_and_outdated_ones_updated()
    {
        var installed = new Dictionary<string, string>
        {
            ["winget"] = @"C:\winget.exe",
            ["yt-dlp"] = @"C:\yt-dlp.exe"
        };
        var service = new ToolchainService(
            (file, arguments, _) => Task.FromResult(file switch
            {
                @"C:\winget.exe" => new ProcessResult(0, "yt-dlp  yt-dlp.yt-dlp  2026.08.19  2026.09.20  winget", string.Empty),
                _ => new ProcessResult(0, "2026.08.19\n", string.Empty)
            }),
            command => installed.GetValueOrDefault(command));

        IReadOnlyList<ToolStatus> statuses = await service.CheckAsync();

        Assert.Multiple(() =>
        {
            Assert.That(statuses[0].CanUpdate, Is.True);
            Assert.That(statuses[0].StatusText, Is.EqualTo("Version 2026.08.19 · update available"));
            Assert.That(statuses[1].CanInstall, Is.True);
            Assert.That(statuses[1].StatusText, Is.EqualTo("Not installed"));
        });
    }

    [TestCase("ffmpeg version 7.1-full_build-www.gyan.dev Copyright (c) 2000-2024", "7.1")]
    [TestCase("deno 2.4.1 (stable, release, x86_64-pc-windows-msvc)", "2.4.1")]
    [TestCase("2026.08.19", "2026.08.19")]
    public void ParseVersion_finds_the_version_number(string output, string expected) =>
        Assert.That(ToolchainService.ParseVersion(output), Is.EqualTo(expected));
}
