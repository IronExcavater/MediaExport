using MediaExport.Pages;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Linq;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace MediaExport;

/// <summary>
/// An empty window that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;

        AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

        AppWindow.TitleBar.ButtonForegroundColor = Colors.White;
        AppWindow.TitleBar.ButtonInactiveForegroundColor = Colors.Gray;

        AppWindow.TitleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(25, 255, 255, 255);
        AppWindow.TitleBar.ButtonHoverForegroundColor = Colors.White;

        navView.Loaded += NavView_Loaded;
    }
    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        var homeItem = navView.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(item => item.Tag?.ToString() == "MediaExport.Pages.ExportPage");

        if (homeItem != null)
        {
            navView.SelectedItem = homeItem;
        }

        navFrame.Navigate(typeof(ExportPage));
    }
    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        navView.IsPaneOpen = !navView.IsPaneOpen;
    }

    private void NavView_SelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            navFrame.Navigate(typeof(SettingsPage));
        }
        else if (args.SelectedItemContainer is NavigationViewItem item)
        {
            Type? page = Type.GetType(item.Tag?.ToString() ?? string.Empty);
            if (page is not null)
                navFrame.Navigate(page);
        }
    }
}