// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Activity;
using MediaExport.Export;
using MediaExport.Search;
using MediaExport.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace MediaExport.Services;

public static partial class ServiceCollectionExtensions
{
    private static IServiceCollection AddPresentation(this IServiceCollection services)
    {
        services.AddSingleton<SearchViewModel>();
        services.AddSingleton<ResultsViewModel>();
        services.AddSingleton<ActivityViewModel>();
        services.AddSingleton<ExportViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<AppViewModels>();
        services.AddSingleton<MainWindow>();
        return services;
    }
}
