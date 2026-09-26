// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaExport.Activity;
using MediaExport.Core.Settings;
using MediaExport.Core.Tooling;
using MediaExport.Search;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel;

namespace MediaExport.Settings;

public sealed record BrowserChoice(string Label, string? Value);

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
        SelectedBrowser = Browsers.FirstOrDefault(browser => browser.Value == settings.Current.CookiesFromBrowser) ?? Browsers[0];
        Theme = settings.Current.Theme;
        UpdateMissingTools();
    }

    public IReadOnlyList<ToolViewModel> Tools { get; }

    public IReadOnlyList<BrowserChoice> Browsers { get; } =
    [
        new("Don't sign in", null), new("Microsoft Edge", "edge"), new("Google Chrome", "chrome"), new("Firefox", "firefox"),
        new("Brave", "brave"), new("Opera", "opera"), new("Vivaldi", "vivaldi")
    ];

    /// <summary><c>System</c>, <c>Light</c> or <c>Dark</c>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSystemTheme), nameof(IsLightTheme), nameof(IsDarkTheme))]
    public partial string Theme { get; set; }

    [ObservableProperty]
    public partial BrowserChoice SelectedBrowser { get; set; }

    /// <summary>Required tools that are missing, as a sentence; <c>null</c> when everything's there.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMissingTools))]
    public partial string? MissingTools { get; private set; }

    [ObservableProperty]
    public partial bool IsInstallingMissing { get; private set; }

    public bool HasMissingTools => MissingTools is not null;

    // Radio buttons bind one of these each.
    public bool IsSystemTheme { get => Theme == "System"; set { if (value) Theme = "System"; } }
    public bool IsLightTheme { get => Theme == "Light"; set { if (value) Theme = "Light"; } }
    public bool IsDarkTheme { get => Theme == "Dark"; set { if (value) Theme = "Dark"; } }

    public string Version
    {
        get
        {
            try
            {
                PackageVersion version = Package.Current.Id.Version;
                return $"{version.Major}.{version.Minor}.{version.Build}";
            }
            catch (System.InvalidOperationException)
            {
                return typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
            }
        }
    }

    public bool HasSearchHistory => _search.Recents.Count > 0;
    public bool HasExportHistory => _activity.Jobs.Any(job => job.IsFinished);

    partial void OnThemeChanged(string value)
    {
        _settings.Current.Theme = value;
        _settings.Save();
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
}
