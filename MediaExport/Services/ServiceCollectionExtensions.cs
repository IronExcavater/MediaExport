// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.DependencyInjection;

namespace MediaExport.Services;

public static partial class ServiceCollectionExtensions
{
    /// <param name="dataFolder">Where settings, history and managed tools are kept.</param>
    public static IServiceCollection AddMediaExport(this IServiceCollection services, string dataFolder) =>
        services
            .AddAppData(dataFolder)
            .AddMedia()
            .AddPresentation();
}
