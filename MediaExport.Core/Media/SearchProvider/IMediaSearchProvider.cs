// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

namespace MediaExport.Core.Media.SearchProvider;

public interface IMediaSearchProvider
{
    string Id { get; }
    string DisplayName { get; }
}