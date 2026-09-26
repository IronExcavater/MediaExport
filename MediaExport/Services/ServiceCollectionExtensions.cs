// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Exporting;
using MediaExport.Core.Media.Providers;
using MediaExport.Core.Media.Ytdlp;
using MediaExport.Export;
using Microsoft.Extensions.DependencyInjection;

namespace MediaExport.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMediaExport(this IServiceCollection services)
    {
        services.AddSingleton<YtdlpService>();
        services.AddSingleton<IMediaDownloader>(provider => provider.GetRequiredService<YtdlpService>());
        services.AddSingleton<IMediaSearchProvider, YouTubeSearchProvider>();
        services.AddSingleton<IMediaSearchProvider, SoundCloudSearchProvider>();
        services.AddSingleton<ExportViewModel>();

        return services;
    }
}
