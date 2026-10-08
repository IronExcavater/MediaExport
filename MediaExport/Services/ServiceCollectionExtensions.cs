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
    /// <param name="dataFolder">Where settings, history and managed tools are kept.</param>
    public static IServiceCollection AddMediaExport(this IServiceCollection services, string dataFolder)
    {
        services.AddSingleton(new SettingsStore(Path.Combine(dataFolder, "settings.json")));
        services.AddSingleton(new ToolchainService(Path.Combine(dataFolder, "Tools")));
        services.AddSingleton<YtdlpService>();
        services.AddSingleton<IMediaDownloader>(provider => provider.GetRequiredService<YtdlpService>());
        services.AddSingleton(_ =>
        {
            var http = new System.Net.Http.HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MediaExport");
            return http;
        });

        // In the order the title bar lists them.
        services.AddSingleton<IMediaSearchProvider, YouTubeSearchProvider>();
        services.AddSingleton<IMediaSearchProvider, YouTubeMusicSearchProvider>();
        services.AddSingleton<IMediaSearchProvider, SoundCloudSearchProvider>();
        services.AddSingleton<IMediaSearchProvider, MixcloudSearchProvider>();
        services.AddSingleton<IMediaSearchProvider, VimeoSearchProvider>();
        services.AddSingleton<IMediaSearchProvider, DailymotionSearchProvider>();
        services.AddSingleton<IMediaSearchProvider, BilibiliSearchProvider>();
        services.AddSingleton<IMediaSearchProvider, NiconicoSearchProvider>();
        services.AddSingleton<IMediaSearchProvider, InternetArchiveSearchProvider>();
        services.AddSingleton<SearchViewModel>();
        services.AddSingleton<ActivityViewModel>();
        services.AddSingleton<ExportViewModel>();
        services.AddSingleton<SettingsViewModel>();

        return services;
    }
}
