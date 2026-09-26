// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaExport.Core.Settings;
using MediaExport.Core.Tooling;
using MediaExport.Export;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace MediaExport.Settings;

public sealed record ThemeChoice(string Label, string Value);

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ToolchainService _toolchain;
    private readonly SettingsStore _settings;

    public SettingsViewModel(ToolchainService toolchain, SettingsStore settings, ExportViewModel export)
    {
        _toolchain = toolchain;
        _settings = settings;
        Export = export;
        SelectedTheme = Themes.FirstOrDefault(theme => theme.Value == settings.Current.Theme) ?? Themes[0];
        MissingTools = Describe(toolchain.FindMissingRequiredTools());
    }

    /// <summary>Export defaults are the export page's own choices, which are remembered.</summary>
    public ExportViewModel Export { get; }

    public IReadOnlyList<ThemeChoice> Themes { get; } =
        [new("Use system setting", "System"), new("Light", "Light"), new("Dark", "Dark")];

    public ObservableCollection<ToolStatus> Tools { get; } = [];

    [ObservableProperty]
    public partial ThemeChoice SelectedTheme { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckToolsCommand), nameof(InstallCommand), nameof(UpdateCommand))]
    public partial bool IsWorking { get; set; }

    [ObservableProperty]
    public partial string ToolStatusText { get; set; } = "Not checked yet";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasToolError))]
    public partial string? ToolError { get; set; }

    /// <summary>Required tools that are missing, as a sentence; <c>null</c> when everything's there.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMissingTools))]
    public partial string? MissingTools { get; set; }

    public bool HasToolError => ToolError is not null;
    public bool HasMissingTools => MissingTools is not null;
    public bool IsWingetMissing => Tools.Count > 0 && !Tools[0].IsWingetAvailable;
    public string Version => typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    partial void OnSelectedThemeChanged(ThemeChoice value)
    {
        _settings.Current.Theme = value.Value;
        _settings.Save();
    }

    [RelayCommand]
    private void ClearRecentResults() => Export.ClearRecentResults();

    private bool CanRunToolAction() => !IsWorking;

    [RelayCommand(CanExecute = nameof(CanRunToolAction))]
    private Task CheckToolsAsync() => RunToolActionAsync(null);

    [RelayCommand(CanExecute = nameof(CanRunToolAction))]
    private Task InstallAsync(ToolStatus status) => RunToolActionAsync(() => _toolchain.InstallAsync(status.Tool));

    [RelayCommand(CanExecute = nameof(CanRunToolAction))]
    private Task UpdateAsync(ToolStatus status) => RunToolActionAsync(() => _toolchain.UpdateAsync(status.Tool));

    private async Task RunToolActionAsync(Func<Task>? action)
    {
        IsWorking = true;
        ToolError = null;
        ToolStatusText = action is null ? "Checking your tools…" : "Working with WinGet…";
        try
        {
            if (action is not null)
                await action();

            IReadOnlyList<ToolStatus> statuses = await _toolchain.CheckAsync();
            Tools.Clear();
            foreach (ToolStatus status in statuses)
                Tools.Add(status);

            MissingTools = Describe(statuses.Where(status => status.Tool.IsRequired && !status.IsInstalled).Select(status => status.Tool).ToArray());
            ToolStatusText = $"Checked {DateTime.Now:t}";
            OnPropertyChanged(nameof(IsWingetMissing));
        }
        catch (Exception exception)
        {
            ToolError = exception.Message;
            ToolStatusText = "Something went wrong";
        }
        finally
        {
            IsWorking = false;
        }
    }

    private static string? Describe(IReadOnlyList<Tool> missing) => missing.Count switch
    {
        0 => null,
        1 => $"{missing[0].Name} is needed to search and export.",
        _ => $"{string.Join(" and ", missing.Select(tool => tool.Name))} are needed to search and export."
    };
}
