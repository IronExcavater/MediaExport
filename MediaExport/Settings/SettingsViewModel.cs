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
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel;

namespace MediaExport.Settings;

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
        Browsers = BrowserCatalog.CreateChoices(BrowserCatalog.Installed(), BrowserCatalog.DefaultBrowser());
        BrowserChoice? automatic = Browsers.FirstOrDefault(browser => browser.Value.StartsWith("auto:", StringComparison.Ordinal));

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
        [new("Same as Windows", "System", "\uE770"), new("Light", "Light", "\uE706"), new("Dark", "Dark", "\uE708")];

    /// <summary>Browsers on this PC that yt-dlp can borrow a sign-in from.</summary>
    public IReadOnlyList<BrowserChoice> Browsers { get; }

    [ObservableProperty]
    public partial SettingsChoice SelectedTheme { get; set; }

    [ObservableProperty]
    public partial BrowserChoice SelectedBrowser { get; set; }

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

    // What the app keeps about what's been done with it, each with how much there is, so it's clear what clearing clears.
    public bool HasRecentSearches => _search.HasRecentSearches;
    public bool HasRecentMedia => _search.HasRecents;
    public bool HasExportHistory => _activity.Jobs.Any(job => job.IsFinished);
    public bool HasAnyHistory => HasRecentSearches || HasRecentMedia || HasExportHistory;
    public string RecentSearchesText => Counted(_search.RecentSearches.Count, "search", "searches");
    public string RecentMediaText => Counted(_search.Recents.Count, "item", "items");
    public string ExportHistoryText => $"{Counted(_activity.Jobs.Count(job => job.IsFinished), "export", "exports")}. The exported files are kept.";

    partial void OnSelectedThemeChanged(SettingsChoice value)
    {
        _settings.Current.Theme = value.Value ?? "System";
        _settings.Save();
        OnPropertyChanged(nameof(Theme));
    }

    partial void OnSelectedBrowserChanged(BrowserChoice value)
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
    private void ClearRecentSearches()
    {
        _search.ClearRecentSearches();
        RefreshHistory();
    }

    [RelayCommand]
    private void ClearRecentMedia()
    {
        _search.ClearRecents();
        RefreshHistory();
    }

    [RelayCommand]
    private void ClearExportHistory()
    {
        _activity.Remove(_activity.Jobs.Where(job => job.IsFinished));
        RefreshHistory();
    }

    [RelayCommand]
    private void ClearAllHistory()
    {
        _search.ClearRecentSearches();
        _search.ClearRecents();
        _activity.Remove(_activity.Jobs.Where(job => job.IsFinished));
        RefreshHistory();
    }

    /// <summary>Counts are read when the page is shown and after anything's cleared; they change elsewhere, not here.</summary>
    public void RefreshHistory() => OnPropertyChanged(string.Empty);

    private static string Counted(int count, string one, string many) => count switch
    {
        0 => "Nothing kept",
        1 => $"1 {one}",
        _ => $"{count} {many}"
    };

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

}
