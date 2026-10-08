// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using MediaExport.Core.Settings;
using MediaExport.Core.Tooling;

namespace MediaExport.Settings;

/// <summary>The app's preferences, and the state of the tools it depends on.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ToolchainService _toolchain;
    private readonly SettingsStore _settings;

    public SettingsViewModel(ToolchainService toolchain, SettingsStore settings)
    {
        _toolchain = toolchain;
        _settings = settings;
    }
}
