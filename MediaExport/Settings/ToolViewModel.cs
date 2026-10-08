// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaExport.Core.Tooling;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MediaExport.Settings;

/// <summary>One tool on the settings page: what's installed, what's out there, and the actions between.</summary>
public sealed partial class ToolViewModel(Tool tool, ToolchainService toolchain, Action changed) : ObservableObject
{
    public Tool Tool { get; } = tool;
    public string Name => Tool.Name;
    public Uri? Website => Tool.Website is { } site ? new Uri(site) : null;
    public Uri SourceCode => new(Tool.SourceUrl);
    public bool HasWebsite => Website is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInstalled), nameof(IsHealthy), nameof(IsMissing), nameof(IsManaged), nameof(CanRemove), nameof(VersionText), nameof(SourceText))]
    [NotifyPropertyChangedFor(nameof(Latest), nameof(HasUpdate), nameof(PrimaryAction), nameof(HasPrimaryAction))]
    public partial ToolStatus? Status { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand), nameof(RemoveCommand))]
    public partial bool IsWorking { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHealthy), nameof(IsMissing), nameof(CanRemove), nameof(HasPrimaryAction))]
    public partial bool IsChecking { get; private set; } = true;

    [ObservableProperty]
    public partial double Progress { get; private set; }

    [ObservableProperty]
    public partial bool IsProgressKnown { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError), nameof(SourceText), nameof(IsHealthy), nameof(IsMissing))]
    public partial string? Error { get; private set; }

    public bool IsInstalled => Status?.IsInstalled == true;
    public bool IsHealthy => !IsChecking && IsInstalled && !HasError;
    public bool IsMissing => !IsChecking && Status is { IsInstalled: false } && !HasError;
    public bool IsManaged => Status?.IsManaged == true;
    public bool CanRemove => !IsChecking && IsManaged;
    public bool IsIdle => !IsWorking;
    public bool HasError => Error is not null;
    /// <summary>The newest release there is to install, once it's been looked up.</summary>
    public ToolRelease? Latest { get; private set; }

    public bool HasUpdate => IsInstalled && Latest is { } latest && Status?.Version is { } version && IsNewer(latest.Version, version);

    /// <summary>Beside the name: the version, whether that's the newest, or that it's missing.</summary>
    public string VersionText => Status switch
    {
        null => string.Empty,
        { IsInstalled: false } => Tool.IsRequired ? "Not installed" : "Not installed (optional)",
        { Version: { } version } => HasUpdate || Latest is null ? version : $"{version}, up to date",
        _ => "Installed"
    };

    /// <summary>The second line: where it's from, or what went wrong.</summary>
    public string? SourceText => Error ?? Status switch
    {
        { IsManaged: true } => "Installed by MediaExport",
        { Path: { } path } => $"Found at {DisplayPath(path)}",
        _ => null
    };

    /// <summary>The one thing most worth doing: install it, or update it.</summary>
    public string? PrimaryAction => this switch
    {
        { IsInstalled: false } => "Install",
        { HasUpdate: true } => $"Update to {Latest!.Version}",
        _ => null
    };

    public bool HasPrimaryAction => !IsChecking && Status is not null && PrimaryAction is not null;

    public async Task RefreshAsync()
    {
        IsChecking = true;
        Error = null;
        ToolStatus? status = null;
        try
        {
            status = await toolchain.CheckAsync(Tool);
            ToolRelease? latest = await toolchain.GetLatestAsync(Tool);
            Status = status;
            Latest = latest;
        }
        catch (Exception exception)
        {
            Status = status;
            Error = $"Couldn't look for updates: {exception.Message}";
        }
        finally
        {
            IsChecking = false;
            RaiseReleaseState();
        }
    }

    private bool CanAct() => !IsWorking;

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task InstallAsync() => RunAsync(() => toolchain.InstallAsync(Tool, new Progress<double>(ReportProgress)));

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task RemoveAsync() => RunAsync(() =>
    {
        toolchain.Remove(Tool);
        return Task.CompletedTask;
    });

    private void ReportProgress(double percent)
    {
        IsProgressKnown = true;
        Progress = percent;
    }

    private async Task RunAsync(Func<Task> action)
    {
        IsWorking = true;
        IsProgressKnown = false;
        Progress = 0;
        Error = null;
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            Error = exception.Message;
        }
        finally
        {
            try
            {
                Status = await toolchain.CheckAsync(Tool);
            }
            catch (Exception exception)
            {
                // Keep the action's error when there is one; it is the useful reason to retry. A failed
                // follow-up probe must never leave the command permanently busy.
                Error ??= $"Couldn't refresh status: {exception.Message}";
            }
            finally
            {
                IsWorking = false;
                RaiseReleaseState();
                changed();
            }
        }
    }

    private void RaiseReleaseState()
    {
        OnPropertyChanged(nameof(Latest));
        OnPropertyChanged(nameof(HasUpdate));
        OnPropertyChanged(nameof(PrimaryAction));
        OnPropertyChanged(nameof(HasPrimaryAction));
        OnPropertyChanged(nameof(VersionText));
    }

    internal static string DisplayPath(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Length == 0 ? path : path[..^extension.Length] + extension.ToLowerInvariant();
    }

    // Tags are dates (yt-dlp) or semantic versions (FFmpeg, Deno); both compare part by part as numbers.
    internal static bool IsNewer(string candidate, string current)
    {
        long[] Parts(string version) => version.Split('.', '-').Select(part => long.TryParse(part, out long number) ? number : 0).ToArray();
        long[] a = Parts(candidate), b = Parts(current);
        for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            long x = i < a.Length ? a[i] : 0, y = i < b.Length ? b[i] : 0;
            if (x != y)
                return x > y;
        }

        return false;
    }
}
