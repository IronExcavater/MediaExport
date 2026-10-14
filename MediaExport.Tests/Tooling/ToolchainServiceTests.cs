// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Settings;
using MediaExport.Core.Tooling;
using MediaExport.Tests.Support;

namespace MediaExport.Tests.Tooling;

public class ToolchainServiceTests
{
    [Test]
    public void AProgramThatIsntInstalledIsntFound()
    {
        var tools = new ToolchainService(
            new SettingsStore(null),
            new ToolchainOptions(Path.GetTempPath(), []),
            new FakeProcessRunner());

        Assert.That(tools.Find("a-tool-nobody-has-installed"), Is.Null);
    }

}
