// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Settings;

namespace MediaExport.Tests.Settings;

public sealed class BrowserCatalogTests
{
    [Test]
    public void Default_browser_is_one_distinct_choice_and_each_browser_carries_its_icon()
    {
        BrowserDefinition[] installed =
        [
            new("Microsoft Edge", "edge", "edge", "MSEdge", "edge-profile"),
            new("Opera", "opera", "opera", "Opera", "opera-profile")
        ];

        IReadOnlyList<BrowserChoice> choices = BrowserCatalog.CreateChoices(installed, "opera");

        Assert.Multiple(() =>
        {
            Assert.That(choices.Select(choice => choice.Label), Is.EqualTo(new[] { "Default browser", "Microsoft Edge", "Opera", "Never" }));
            Assert.That(choices.Select(choice => choice.Value), Is.EqualTo(new[] { "auto:opera", "edge", "opera", "never" }));
            Assert.That(choices.Select(choice => choice.Icon), Is.EqualTo(new string?[] { "opera", "edge", "opera", null }));
        });
    }

    [Test]
    public void Browser_definitions_use_the_icon_names_shipped_by_the_dev_project()
    {
        IReadOnlyDictionary<string, string> icons = BrowserCatalog.Definitions("local", "roaming")
            .ToDictionary(browser => browser.Value, browser => browser.Icon);

        Assert.Multiple(() =>
        {
            Assert.That(icons["edge"], Is.EqualTo("microsoft-edge"));
            Assert.That(icons["chrome"], Is.EqualTo("google-chrome"));
            Assert.That(icons["firefox"], Is.EqualTo("mozilla-firefox"));
            Assert.That(icons["opera"], Is.EqualTo("opera"));
            Assert.That(icons["brave"], Is.EqualTo("brave"));
            Assert.That(icons["vivaldi"], Is.EqualTo("vivaldi"));
        });
    }
}
