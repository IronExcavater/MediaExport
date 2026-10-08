// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

namespace MediaExport.Settings;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;

public sealed record BrowserDefinition(string Label, string Value, string Icon, string ProgId, string Profile);

public sealed record BrowserChoice(string Label, string Value, string? Icon)
{
    public override string ToString() => Label;
}

public static class BrowserCatalog
{
    public static IReadOnlyList<BrowserDefinition> Installed()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Definitions(local, roaming).Where(browser => Directory.Exists(browser.Profile)).ToArray();
    }

    [SupportedOSPlatform("windows")]
    public static string? DefaultBrowser()
    {
        using Microsoft.Win32.RegistryKey? choice = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice");
        string program = choice?.GetValue("ProgId") as string ?? string.Empty;
        return Definitions(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData))
            .FirstOrDefault(browser => program.StartsWith(browser.ProgId, StringComparison.OrdinalIgnoreCase))?.Value;
    }

    public static IReadOnlyList<BrowserChoice> CreateChoices(IEnumerable<BrowserDefinition> installed, string? defaultBrowser)
    {
        BrowserDefinition[] browsers = installed.ToArray();
        var choices = new List<BrowserChoice>();
        if (browsers.FirstOrDefault(browser => browser.Value == defaultBrowser) is { } automatic)
            choices.Add(new BrowserChoice("Default browser", $"auto:{automatic.Value}", automatic.Icon));
        choices.AddRange(browsers.Select(browser => new BrowserChoice(browser.Label, browser.Value, browser.Icon)));
        choices.Add(new BrowserChoice("Never", "never", null));
        return choices;
    }

    internal static BrowserDefinition[] Definitions(string local, string roaming) =>
    [
        new("Microsoft Edge", "edge", "microsoft-edge", "MSEdge", Path.Combine(local, "Microsoft", "Edge", "User Data")),
        new("Google Chrome", "chrome", "google-chrome", "Chrome", Path.Combine(local, "Google", "Chrome", "User Data")),
        new("Firefox", "firefox", "mozilla-firefox", "Firefox", Path.Combine(roaming, "Mozilla", "Firefox", "Profiles")),
        new("Brave", "brave", "brave", "Brave", Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data")),
        new("Opera", "opera", "opera", "Opera", Path.Combine(roaming, "Opera Software", "Opera Stable")),
        new("Vivaldi", "vivaldi", "vivaldi", "Vivaldi", Path.Combine(local, "Vivaldi", "User Data")),
        new("Chromium", "chromium", "chromium", "Chromium", Path.Combine(local, "Chromium", "User Data"))
    ];
}
