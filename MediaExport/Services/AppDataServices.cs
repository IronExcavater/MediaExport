// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Settings;
using MediaExport.Core.Tooling;
using Microsoft.Extensions.DependencyInjection;
using System.IO;

namespace MediaExport.Services;

public static partial class ServiceCollectionExtensions
{
    private static IServiceCollection AddAppData(this IServiceCollection services, string dataFolder)
    {
        services.AddSingleton(new SettingsStore(Path.Combine(dataFolder, "settings.json")));
        services.AddSingleton(new ToolchainService(Path.Combine(dataFolder, "Tools")));
        return services;
    }
}
