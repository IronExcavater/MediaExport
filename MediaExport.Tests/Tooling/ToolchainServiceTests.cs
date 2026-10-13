// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Settings;
using MediaExport.Core.Tooling;

namespace MediaExport.Tests.Tooling;

public class ToolchainServiceTests
{
    [Test]
    public void AProgramThatIsntInstalledIsntFound()
    {
        var tools = new ToolchainService(new SettingsStore(null));

        Assert.That(tools.Locate("a-tool-nobody-has-installed"), Is.Null);
    }

    [TestCase("ffmpeg version 7.1-full_build-www.gyan.dev Copyright (c) 2000-2024", "7.1")]
    [TestCase("2026.09.01", "2026.09.01")]
    [TestCase("no version here", null)]
    public void TheVersionIsPickedOutOfWhatAToolPrints(string output, string? version)
    {
        Assert.That(ToolchainService.ParseVersion(output), Is.EqualTo(version));
    }
}
