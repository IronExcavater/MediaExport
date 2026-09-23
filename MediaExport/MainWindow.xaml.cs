using MediaExport.Pages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

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
    }

    private void Navigation_ItemInvoked(
        NavigationView sender,
        NavigationViewItemInvokedEventArgs args)
    {
        Type? page = GetPageType(args);

        if (page is null || ContentFrame.CurrentSourcePageType == page)
            return;

        ContentFrame.Navigate(page);
    }

    private static Type? GetPageType(NavigationViewItemInvokedEventArgs args)
    {
        if (args.IsSettingsInvoked)
            return typeof(SettingsPage);
        
        switch (args.InvokedItemContainer?.Tag?.ToString())
        {
            case "export":
                return typeof(ExportPage);
            case "activity":
                return typeof(ActivityPage);
            default:
                return null;
        }
    }
}