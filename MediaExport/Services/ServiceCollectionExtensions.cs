// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media;
using MediaExport.Core.Tools;
using MediaExport.Export;
using Microsoft.Extensions.DependencyInjection;

namespace MediaExport.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMediaExport(this IServiceCollection services)
    {
        services.AddSingleton<MediaService>();
        services.AddSingleton<ToolchainService>();

        // Retain viewModel states between page visits
        services.AddSingleton<ExportViewModel>();

        return services;
    }
}
