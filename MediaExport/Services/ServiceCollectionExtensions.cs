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
using System.Net.Http;

namespace MediaExport.Services;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the app's services as singletons, so a page's view model is as it was left.</summary>
    /// <param name="dataFolder">Where settings, remembered searches and the tools the app installs are kept.</param>
    public static IServiceCollection AddMediaExport(this IServiceCollection services, string dataFolder)
    {
        services.AddSingleton(new SettingsStore(Path.Combine(dataFolder, "settings.json")));
        services.AddSingleton<ToolchainService>();
        services.AddSingleton<YtdlpService>();
        services.AddSingleton<IMediaDownloader>(provider => provider.GetRequiredService<YtdlpService>());

        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IFolderLauncher, WindowsFolderLauncher>();

        services.AddSingleton(new SearchCache(Path.Combine(dataFolder, "search-cache-2.json")));

        // SoundCloud's website turns away a request that doesn't say what browser it is from.
        var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
        services.AddSingleton(new SoundCloudCharts(http));

        // Search providers, in the order they are listed in the search field.
        services.AddSingleton<IMediaSearchProvider, YouTubeSearchProvider>();
        services.AddSingleton<IMediaSearchProvider, SoundCloudSearchProvider>();

        services.AddSingleton<ExportViewModel>();
        services.AddSingleton<SearchViewModel>();
        services.AddSingleton<ActivityViewModel>();
        services.AddSingleton<SettingsViewModel>();

        return services;
    }
}
