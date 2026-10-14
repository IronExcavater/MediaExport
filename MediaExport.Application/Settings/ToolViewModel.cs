// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaExport.Core.Tooling;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace MediaExport.Settings;

/// <summary>One tool's row on the Settings page: whether it is installed, which version, and by whom.</summary>
public sealed partial class ToolViewModel(ToolInfo tool, ToolchainService toolchain) : ObservableObject
{
    public string Name => tool.Name;
    public Uri Website => tool.Website;
    public Uri SourceCode => tool.SourceCode;

    /// <summary>Whether the tool is being looked for, which it is until the page first shows.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHealthy), nameof(IsMissing), nameof(CanInstall))]
    [NotifyPropertyChangedFor(nameof(CanUpdate))]
    [NotifyCanExecuteChangedFor(nameof(UpdateCommand))]
    public partial bool IsChecking { get; private set; } = true;

    /// <summary>The program's full path, or <c>null</c> if it isn't installed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInstalled), nameof(IsManaged), nameof(IsHealthy), nameof(IsMissing))]
    [NotifyPropertyChangedFor(nameof(CanInstall), nameof(CanUpdate), nameof(VersionText), nameof(SourceText), nameof(RemoveTip))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand), nameof(UpdateCommand))]
    public partial string? ProgramPath { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VersionText))]
    public partial string? Version { get; private set; }

    /// <summary>Why installing or removing it failed, or <c>null</c> if neither has.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError), nameof(IsHealthy), nameof(IsMissing), nameof(SourceText))]
    public partial string? Error { get; private set; }

    public bool IsInstalled => ProgramPath is not null;

    /// <summary>
    /// Whether the copy in use is one the app installed. Only that copy is the app's to update or remove; one
    /// installed outside the app is looked after by whatever installed it.
    /// </summary>
    public bool IsManaged => ProgramPath is not null && toolchain.IsManaged(ProgramPath);

    public bool HasError => Error is not null;
    public bool IsHealthy => !IsChecking && !HasError && IsInstalled;
    public bool IsMissing => !IsChecking && !HasError && !IsInstalled;

    /// <summary>Whether there is an Install button: the tool is missing and can be downloaded as one file.</summary>
    public bool CanInstall => !IsChecking && !IsInstalled && tool.DownloadUrl is not null;

    /// <summary>Whether Update all does anything for this tool.</summary>
    public bool CanUpdate => !IsChecking && IsManaged && tool.DownloadUrl is not null;

    /// <summary>Shown beside the name: the version, or that the tool is missing.</summary>
    public string VersionText
    {
        get
        {
            if (!IsInstalled)
                return "Not installed";

            return Version ?? "Installed";
        }
    }

    /// <summary>Shown under the name: who installed the tool and where, or what went wrong.</summary>
    public string SourceText
    {
        get
        {
            if (Error is not null)
                return Error;

            if (!IsInstalled)
                return "Not found in MediaExport's tools or on PATH";

            return IsManaged ? "Installed by MediaExport" : $"Installed outside MediaExport, at {ProgramPath}";
        }
    }

    /// <summary>What the remove button says when the pointer is over it, which is why it can or can't be used.</summary>
    public string RemoveTip => IsManaged
        ? "Remove the copy MediaExport installed"
        : "Installed outside MediaExport, so it is updated and removed where it was installed";

    /// <summary>Looks for the tool again and asks the copy that is found for its version.</summary>
    public async Task RefreshAsync()
    {
        IsChecking = true;
        ProgramPath = toolchain.Find(tool.Id);
        Version = ProgramPath is null ? null : await toolchain.ReadVersionAsync(ProgramPath, tool.VersionArgument);
        IsChecking = false;
    }

    /// <summary>Updates this tool when it is a copy MediaExport installed.</summary>
    [RelayCommand(CanExecute = nameof(CanUpdate))]
    private async Task UpdateAsync()
    {
        if (!CanUpdate)
            return;

        await toolchain.UpdateAsync(tool.Id, tool.DownloadUrl!);
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

    /// <summary>Deletes the copy of the tool that the app installed.</summary>
    [RelayCommand(CanExecute = nameof(IsManaged))]
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
}
