// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaExport.Core.Tooling;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace MediaExport.Settings;

/// <summary>A program the app depends on, as the Settings page lists it.</summary>
/// <param name="Id">Its key in the settings' table of tools, which is also its program's name.</param>
/// <param name="VersionArgument">What makes the program print its version.</param>
/// <param name="DownloadUrl">Where its program can be downloaded as one file, or <c>null</c> if it can't be.</param>
public sealed record ToolInfo(
    string Id,
    string Name,
    string VersionArgument,
    Uri Website,
    Uri SourceCode,
    string? DownloadUrl);

/// <summary>One tool's row on the Settings page: whether it is installed, which version, and where it was found.</summary>
public sealed partial class ToolViewModel(ToolInfo tool, ToolchainService toolchain) : ObservableObject
{
    public string Name => tool.Name;

    /// <summary>Whether the tool is being looked for, which it is until the page first shows.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHealthy), nameof(IsMissing), nameof(CanInstall))]
    public partial bool IsChecking { get; private set; } = true;

    /// <summary>The program's full path, or <c>null</c> if it isn't installed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHealthy), nameof(IsMissing), nameof(CanInstall), nameof(VersionText), nameof(SourceText))]
    [NotifyCanExecuteChangedFor(nameof(ShowInFolderCommand), nameof(RemoveCommand))]
    public partial string? ProgramPath { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VersionText))]
    public partial string? Version { get; private set; }

    /// <summary>Why installing or removing it failed, or <c>null</c> if neither has.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError), nameof(IsHealthy), nameof(IsMissing), nameof(SourceText))]
    public partial string? Error { get; private set; }

    public bool HasError => Error is not null;
    public bool IsHealthy => !IsChecking && !HasError && ProgramPath is not null;
    public bool IsMissing => !IsChecking && !HasError && ProgramPath is null;

    /// <summary>Whether there is an Install button: the tool is missing and can be downloaded as one file.</summary>
    public bool CanInstall => !IsChecking && ProgramPath is null && tool.DownloadUrl is not null;

    /// <summary>Shown beside the name: the version, or that the tool is missing.</summary>
    public string VersionText
    {
        get
        {
            if (ProgramPath is null)
                return "Not installed";

            return Version ?? "Installed";
        }
    }

    /// <summary>Shown under the name: where the tool was found, or what went wrong.</summary>
    public string SourceText
    {
        get
        {
            if (Error is not null)
                return Error;

            if (ProgramPath is null)
                return "Not found in MediaExport's tools or on PATH";

            return toolchain.IsManaged(ProgramPath) ? "Installed by MediaExport" : $"Found at {ProgramPath}";
        }
    }

    /// <summary>Looks for the tool again and asks the copy that is found for its version.</summary>
    public async Task RefreshAsync()
    {
        IsChecking = true;
        ProgramPath = toolchain.Find(tool.Id);
        Version = ProgramPath is null ? null : await toolchain.ReadVersionAsync(ProgramPath, tool.VersionArgument);
        IsChecking = false;
    }

    [RelayCommand]
    private void OpenWebsite() => OpenLink(tool.Website);

    [RelayCommand]
    private void OpenSourceCode() => OpenLink(tool.SourceCode);

    // Windows opens a link in the default browser when it is started as if it were a program.
    private static void OpenLink(Uri link) => Process.Start(new ProcessStartInfo(link.AbsoluteUri) { UseShellExecute = true });

    private bool IsInstalled() => ProgramPath is not null;

    [RelayCommand(CanExecute = nameof(IsInstalled))]
    private void ShowInFolder() => Process.Start("explorer.exe", $"/select,\"{ProgramPath}\"");

    // Only a copy the app installed is the app's to delete. Another is removed where it was installed.
    private bool CanRemove() => ProgramPath is not null && toolchain.IsManaged(ProgramPath);

    /// <summary>Deletes the copy of the tool that the app installed.</summary>
    [RelayCommand(CanExecute = nameof(CanRemove))]
    private async Task RemoveAsync()
    {
        Error = null;
        try
        {
            await toolchain.RemoveAsync(tool.Id);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Error = $"Couldn't remove {Name}. {exception.Message}";
        }

        await RefreshAsync();
    }

    /// <summary>Updates the copy the app installed. A tool found elsewhere is left to whatever installed it.</summary>
    public async Task UpdateAsync()
    {
        if (tool.DownloadUrl is null || !CanRemove())
            return;

        await toolchain.UpdateAsync(tool.Id, tool.DownloadUrl);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task InstallAsync()
    {
        if (tool.DownloadUrl is null)
            return;

        IsChecking = true;
        Error = null;
        try
        {
            await toolchain.EnsureInstalledAsync(tool.Id, tool.DownloadUrl);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            Error = $"Couldn't install {Name}. {exception.Message}";
        }

        await RefreshAsync();
    }
}
