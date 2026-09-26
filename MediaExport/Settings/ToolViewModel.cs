// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaExport.Core.Tooling;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace MediaExport.Settings;

/// <summary>One tool on the settings page: what's installed, what's out there, and the actions between.</summary>
public sealed partial class ToolViewModel(Tool tool, ToolchainService toolchain, Action changed) : ObservableObject
{
    public Tool Tool { get; } = tool;
    public string Name => Tool.Name;
    public string Purpose => Tool.Purpose;

    /// <summary>Recent releases, newest first, for picking a version.</summary>
    public ObservableCollection<ToolRelease> Releases { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInstalled), nameof(IsHealthy), nameof(IsMissing), nameof(IsManaged), nameof(StatusText), nameof(Summary), nameof(Latest), nameof(HasUpdate), nameof(PrimaryAction), nameof(HasPrimaryAction))]
    public partial ToolStatus? Status { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand), nameof(InstallVersionCommand), nameof(RemoveCommand))]
    public partial bool IsWorking { get; private set; }

    [ObservableProperty]
    public partial double Progress { get; private set; }

    [ObservableProperty]
    public partial bool IsProgressKnown { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError), nameof(Summary), nameof(IsHealthy), nameof(IsMissing))]
    public partial string? Error { get; private set; }

    /// <summary>The status line: what went wrong if something did, otherwise what's installed.</summary>
    public string Summary => Error ?? StatusText;

    public bool IsInstalled => Status?.IsInstalled == true;
    public bool IsHealthy => IsInstalled && !HasError;
    public bool IsMissing => Status is { IsInstalled: false } && !HasError;
    public bool IsManaged => Status?.IsManaged == true;
    public bool IsIdle => !IsWorking;
    public bool HasError => Error is not null;
    public ToolRelease? Latest => Releases.FirstOrDefault();
    public bool HasUpdate => IsInstalled && Latest is { } latest && Status?.Version is { } version && IsNewer(latest.Version, version);

    public string StatusText => Status switch
    {
        null => "Checking…",
        { IsInstalled: false } => Tool.IsRequired ? "Not installed · needed to export" : "Not installed · optional",
        { IsManaged: true } => $"Version {Status.Version}",
        _ => $"Version {Status.Version ?? "unknown"} · installed outside MediaExport"
    };

    /// <summary>The one thing most worth doing: install it, update it, or take over a copy found on PATH.</summary>
    public string? PrimaryAction => this switch
    {
        { IsInstalled: false } => "Install",
        { HasUpdate: true } => $"Update to {Latest!.Version}",
        _ => null
    };

    public bool HasPrimaryAction => PrimaryAction is not null;

    public async Task RefreshAsync()
    {
        Error = null;
        try
        {
            Status = await toolchain.CheckAsync(Tool);
            IReadOnlyList<ToolRelease> releases = await toolchain.GetReleasesAsync(Tool);
            Releases.Clear();
            foreach (ToolRelease release in releases)
                Releases.Add(release);
        }
        catch (Exception exception)
        {
            Error = $"Couldn't check for versions: {exception.Message}";
        }

        OnPropertyChanged(nameof(Latest));
        OnPropertyChanged(nameof(HasUpdate));
        OnPropertyChanged(nameof(PrimaryAction));
        OnPropertyChanged(nameof(HasPrimaryAction));
    }

    private bool CanAct() => !IsWorking;

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task InstallAsync() => RunAsync(() => toolchain.InstallAsync(Tool, null, new Progress<double>(ReportProgress)));

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task InstallVersionAsync(ToolRelease release) => RunAsync(() => toolchain.InstallAsync(Tool, release, new Progress<double>(ReportProgress)));

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
            Status = await toolchain.CheckAsync(Tool);
            IsWorking = false;
            OnPropertyChanged(nameof(HasUpdate));
            OnPropertyChanged(nameof(PrimaryAction));
            OnPropertyChanged(nameof(HasPrimaryAction));
            changed();
        }
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
