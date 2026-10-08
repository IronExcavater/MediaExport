// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Exporting;
using MediaExport.Core.Media.Providers;
using MediaExport.Core.Media.Ytdlp;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;

namespace MediaExport.Services;

public static partial class ServiceCollectionExtensions
{
    private static IServiceCollection AddMedia(this IServiceCollection services)
    {
        services.AddSingleton<YtdlpService>();
        services.AddSingleton<IMediaDownloader>(provider => provider.GetRequiredService<YtdlpService>());
        services.AddSingleton(_ => CreateHttpClient());
        services.AddSingleton<YouTubeCharts>();
        services.AddSingleton<SoundCloudCharts>();

        // This order is also the order shown in the source picker.
        services.AddSingleton<IMediaSearchProvider, YouTubeSearchProvider>();
        services.AddSingleton<IMediaSearchProvider, YouTubeMusicSearchProvider>();
        services.AddSingleton<IMediaSearchProvider, SoundCloudSearchProvider>();
        services.AddSingleton<IMediaSearchProvider, MixcloudSearchProvider>();
        services.AddSingleton<IMediaSearchProvider, VimeoSearchProvider>();
        services.AddSingleton<IMediaSearchProvider, DailymotionSearchProvider>();
        services.AddSingleton<IMediaSearchProvider, BilibiliSearchProvider>();
        services.AddSingleton<IMediaSearchProvider, NiconicoSearchProvider>();
        services.AddSingleton<IMediaSearchProvider, InternetArchiveSearchProvider>();
        return services;
    }

    private static HttpClient CreateHttpClient()
    {
        var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MediaExport");
        return http;
    }
}
