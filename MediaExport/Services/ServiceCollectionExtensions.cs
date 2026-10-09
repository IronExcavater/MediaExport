// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Activity;
using MediaExport.Core.Exporting;
using MediaExport.Core.Media.Providers;
using MediaExport.Core.Media.Ytdlp;
using MediaExport.Core.Settings;
using MediaExport.Core.Tooling;
using MediaExport.Export;
using MediaExport.Search;
using MediaExport.Settings;
using Microsoft.Extensions.DependencyInjection;
using System.IO;

namespace MediaExport.Services;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers what the app is made of. Everything is a singleton: one of each for the app's life, so a page
    /// that's left and come back to finds its view model as it was. Each asks for what it needs in its constructor.
    /// </summary>
    /// <param name="dataFolder">Where settings and the tools the app installs are kept.</param>
    public static IServiceCollection AddMediaExport(this IServiceCollection services, string dataFolder)
    {
        services.AddSingleton(new SettingsStore(Path.Combine(dataFolder, "settings.json")));
        services.AddSingleton(new ToolchainService(Path.Combine(dataFolder, "Tools")));
        services.AddSingleton<YtdlpService>();
        services.AddSingleton<IMediaDownloader>(provider => provider.GetRequiredService<YtdlpService>());

        // Search providers, in the order they are listed in the search field.
        services.AddSingleton<IMediaSearchProvider, YouTubeSearchProvider>();

        services.AddSingleton<ExportViewModel>();
        services.AddSingleton<SearchViewModel>();
        services.AddSingleton<ActivityViewModel>();
        services.AddSingleton<SettingsViewModel>();

        return services;
    }
}
