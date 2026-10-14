// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaExport.Core.Tooling;

namespace MediaExport.Settings;

/// <summary>One tool's installation, version, ownership, and available actions.</summary>
public sealed partial class ToolViewModel(ToolInfo tool, ToolchainService toolchain) : ObservableObject
{
    public string Id => tool.Id;
    public string Name => tool.Name;
    public Uri Website => tool.Website;
    public Uri SourceCode => tool.SourceCode;
    public bool SupportsManagedInstall => tool.DownloadUri is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInstalled), nameof(IsExternal), nameof(IsManaged))]
    [NotifyPropertyChangedFor(nameof(IsHealthy), nameof(IsMissing), nameof(CanInstall), nameof(CanUpdate), nameof(CanRemove))]
    [NotifyPropertyChangedFor(nameof(InstallActionText), nameof(VersionText), nameof(SourceText), nameof(RemoveTip))]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand), nameof(UpdateCommand), nameof(RemoveCommand))]
    public partial ToolInstallation? Installation { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VersionText))]
    public partial string? Version { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError), nameof(IsHealthy), nameof(IsMissing), nameof(SourceText))]
    public partial string? Error { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHealthy), nameof(IsMissing), nameof(CanInstall), nameof(CanUpdate), nameof(CanRemove))]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand), nameof(UpdateCommand), nameof(RemoveCommand))]
    public partial bool IsChecking { get; private set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHealthy), nameof(IsMissing), nameof(CanInstall), nameof(CanUpdate), nameof(CanRemove))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand), nameof(InstallCommand), nameof(UpdateCommand), nameof(RemoveCommand))]
    public partial bool IsBusy { get; private set; }

    public bool IsInstalled => Installation is not null;
    public bool IsExternal => Installation?.Origin == ToolOrigin.External;
    public bool IsManaged => Installation?.Origin == ToolOrigin.Managed;
    public bool HasError => Error is not null;
    public bool IsHealthy => !IsBusy && !IsChecking && !HasError && IsInstalled;
    public bool IsMissing => !IsBusy && !IsChecking && !HasError && !IsInstalled;
    public bool CanInstall => !IsBusy && !IsChecking && !IsManaged && tool.DownloadUri is not null;
    public bool CanUpdate => !IsBusy && !IsChecking && IsManaged && tool.DownloadUri is not null;
    public bool CanRemove => !IsBusy && !IsChecking && IsManaged;
    public string InstallActionText => IsExternal ? "Install managed copy" : "Install";

    public string VersionText => IsInstalled ? Version ?? "Installed" : "Not installed";

    public string SourceText
    {
        get
        {
            if (Error is not null)
                return Error;
            if (Installation is null)
                return "Not found in MediaExport's tools or on PATH";
            if (IsManaged)
                return $"Installed by MediaExport at {Installation.Path}";

            return $"Using a tool installed outside MediaExport at {Installation.Path}. "
                + "Updates and removal are handled where it was installed.";
        }
    }

    public string RemoveTip => IsManaged
        ? "Remove the copy MediaExport installed"
        : "Only tools installed by MediaExport can be removed here";

    private bool CanStartOperation() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanStartOperation), IncludeCancelCommand = true)]
    private Task RefreshAsync(CancellationToken cancellationToken) =>
        RunOperationAsync("inspect", RefreshCoreAsync, cancellationToken);

    [RelayCommand(CanExecute = nameof(CanInstall), IncludeCancelCommand = true)]
    private Task InstallAsync(CancellationToken cancellationToken) =>
        RunOperationAsync("install", async token =>
        {
            await toolchain.InstallAsync(tool.Id, tool.DownloadUri!, token);
            await RefreshCoreAsync(token);
        }, cancellationToken);

    [RelayCommand(CanExecute = nameof(CanUpdate), IncludeCancelCommand = true)]
    private Task UpdateAsync(CancellationToken cancellationToken) =>
        RunOperationAsync("update", async token =>
        {
            await toolchain.UpdateAsync(tool.Id, tool.DownloadUri!, token);
            await RefreshCoreAsync(token);
        }, cancellationToken);

    [RelayCommand(CanExecute = nameof(CanRemove), IncludeCancelCommand = true)]
    private Task RemoveAsync(CancellationToken cancellationToken) =>
        RunOperationAsync("remove", async token =>
        {
            await toolchain.RemoveAsync(tool.Id, token);
            await RefreshCoreAsync(token);
        }, cancellationToken);

    private async Task RefreshCoreAsync(CancellationToken cancellationToken)
    {
        IsChecking = true;
        try
        {
            Installation = toolchain.Find(tool.Id);
            Version = Installation is null
                ? null
                : await toolchain.ReadVersionAsync(Installation, tool.VersionArgument, cancellationToken);
        }
        finally
        {
            IsChecking = false;
        }
    }

    private async Task RunOperationAsync(
        string action,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        if (IsBusy)
            return;

        IsBusy = true;
        Error = null;
        try
        {
            await operation(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            Error = $"Couldn't {action} {Name}. {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static bool IsExpectedFailure(Exception exception) => exception is
        ToolInspectionException or
        HttpRequestException or
        IOException or
        UnauthorizedAccessException or
        InvalidOperationException;
}
