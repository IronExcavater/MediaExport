// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaExport.Activity;
using MediaExport.Core.Settings;
using MediaExport.Core.Tooling;
using MediaExport.Search;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel;

namespace MediaExport.Settings;

public sealed record SettingsChoice(string Label, string? Value)
{
    public override string ToString() => Label;
}

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ToolchainService _toolchain;
    private readonly SettingsStore _settings;
    private readonly SearchViewModel _search;
    private readonly ActivityViewModel _activity;

    public SettingsViewModel(ToolchainService toolchain, SettingsStore settings, SearchViewModel search, ActivityViewModel activity)
    {
        _toolchain = toolchain;
        _settings = settings;
        _search = search;
        _activity = activity;
        Tools = ToolchainService.Tools.Select(tool => new ToolViewModel(tool, toolchain, UpdateMissingTools)).ToArray();
        SettingsChoice[] installed = FindInstalledBrowsers().ToArray();
        string? defaultBrowser = FindDefaultBrowser();
        SettingsChoice? automatic = installed.FirstOrDefault(browser => browser.Value == defaultBrowser) is { } found
            ? new SettingsChoice($"{found.Label}, only when needed", $"auto:{found.Value}")
            : null;
        Browsers = [.. automatic is null ? [] : new[] { automatic }, .. installed, new SettingsChoice("Never", "never")];

        // "auto" follows whichever browser is the default now, in case it changed.
        string? saved = settings.Current.CookiesFromBrowser;
        SelectedBrowser = (saved is null || saved.StartsWith("auto", StringComparison.Ordinal)) && automatic is not null
            ? automatic
            : Browsers.FirstOrDefault(browser => browser.Value == saved) ?? Browsers[^1];
        if (SelectedBrowser == automatic && saved != automatic.Value)
        {
            settings.Current.CookiesFromBrowser = automatic.Value;
            settings.Save();
        }
        SelectedTheme = Themes.FirstOrDefault(theme => theme.Value == settings.Current.Theme) ?? Themes[0];
        UpdateMissingTools();
    }

    public IReadOnlyList<ToolViewModel> Tools { get; }

    public IReadOnlyList<SettingsChoice> Themes { get; } =
        [new("Same as Windows", "System"), new("Light", "Light"), new("Dark", "Dark")];

    /// <summary>Browsers on this PC that yt-dlp can borrow a sign-in from.</summary>
    public IReadOnlyList<SettingsChoice> Browsers { get; }

    [ObservableProperty]
    public partial SettingsChoice SelectedTheme { get; set; }

    [ObservableProperty]
    public partial SettingsChoice SelectedBrowser { get; set; }

    /// <summary>Required tools that are missing, as a sentence; <c>null</c> when everything's there.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMissingTools))]
    public partial string? MissingTools { get; private set; }

    [ObservableProperty]
    public partial bool IsInstallingMissing { get; private set; }

    public bool HasMissingTools => MissingTools is not null;

    /// <summary><c>System</c>, <c>Light</c> or <c>Dark</c>.</summary>
    public string Theme => SelectedTheme.Value ?? "System";

    public string Version
    {
        get
        {
            try
            {
                PackageVersion version = Package.Current.Id.Version;
                return $"{version.Major}.{version.Minor}.{version.Build}";
            }
            catch (InvalidOperationException)
            {
                return typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
            }
        }
    }

    public bool HasSearchHistory => _search.Recents.Count > 0;
    public bool HasExportHistory => _activity.Jobs.Any(job => job.IsFinished);

    partial void OnSelectedThemeChanged(SettingsChoice value)
    {
        _settings.Current.Theme = value.Value ?? "System";
        _settings.Save();
        OnPropertyChanged(nameof(Theme));
    }

    partial void OnSelectedBrowserChanged(SettingsChoice value)
    {
        _settings.Current.CookiesFromBrowser = value.Value;
        _settings.Save();
    }

    public Task RefreshToolsAsync() => Task.WhenAll(Tools.Select(tool => tool.RefreshAsync()));

    /// <summary>Installs every missing required tool, for the banner's one-click setup.</summary>
    [RelayCommand]
    private async Task InstallMissingToolsAsync()
    {
        IsInstallingMissing = true;
        try
        {
            foreach (ToolViewModel tool in Tools.Where(tool => tool.Tool.IsRequired && _toolchain.FindMissingRequiredTools().Contains(tool.Tool)))
                await tool.InstallCommand.ExecuteAsync(null);
        }
        finally
        {
            IsInstallingMissing = false;
            UpdateMissingTools();
        }
    }

    [RelayCommand]
    private void ClearSearchHistory()
    {
        _search.ClearRecents();
        OnPropertyChanged(nameof(HasSearchHistory));
    }

    [RelayCommand]
    private void ClearExportHistory()
    {
        _activity.Remove(_activity.Jobs.Where(job => job.IsFinished));
        OnPropertyChanged(nameof(HasExportHistory));
    }

    public void RefreshHistory()
    {
        OnPropertyChanged(nameof(HasSearchHistory));
        OnPropertyChanged(nameof(HasExportHistory));
    }

    private void UpdateMissingTools()
    {
        IReadOnlyList<Tool> missing = _toolchain.FindMissingRequiredTools();
        MissingTools = missing.Count switch
        {
            0 => null,
            1 => $"{missing[0].Name} is needed to search and export.",
            _ => $"{string.Join(" and ", missing.Select(tool => tool.Name))} are needed to search and export."
        };
    }

    // Browsers yt-dlp can borrow a sign-in from: its name for each, how Windows names it as the link handler, and
    // its profile folder, which is where the cookies are read from and so says whether it's installed.
    private static (string Label, string Value, string ProgId, string Profile)[] KnownBrowsers()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return
        [
            ("Microsoft Edge", "edge", "MSEdge", Path.Combine(local, "Microsoft", "Edge", "User Data")),
            ("Google Chrome", "chrome", "Chrome", Path.Combine(local, "Google", "Chrome", "User Data")),
            ("Firefox", "firefox", "Firefox", Path.Combine(roaming, "Mozilla", "Firefox", "Profiles")),
            ("Brave", "brave", "Brave", Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data")),
            ("Opera", "opera", "Opera", Path.Combine(roaming, "Opera Software", "Opera Stable")),
            ("Vivaldi", "vivaldi", "Vivaldi", Path.Combine(local, "Vivaldi", "User Data")),
            ("Chromium", "chromium", "Chromium", Path.Combine(local, "Chromium", "User Data"))
        ];
    }

    private static IEnumerable<SettingsChoice> FindInstalledBrowsers() =>
        KnownBrowsers().Where(browser => Directory.Exists(browser.Profile)).Select(browser => new SettingsChoice(browser.Label, browser.Value));

    /// <summary>The browser Windows opens web links with, as yt-dlp names it.</summary>
    private static string? FindDefaultBrowser()
    {
        using Microsoft.Win32.RegistryKey? choice = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice");
        string program = choice?.GetValue("ProgId") as string ?? string.Empty;
        return KnownBrowsers().FirstOrDefault(browser => program.StartsWith(browser.ProgId, StringComparison.OrdinalIgnoreCase)).Value;
    }
}
