// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace MediaExport.Search;

public sealed partial class SearchPage : Page
{
    public SearchPage()
    {
        InitializeComponent();
    }

    public SearchViewModel ViewModel { get; } = App.Current.Services.GetRequiredService<SearchViewModel>();
}
