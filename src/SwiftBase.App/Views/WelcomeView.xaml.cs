using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SwiftBase.Models;
using SwiftBase.ViewModels;

namespace SwiftBase.Views;

public partial class WelcomeView : UserControl
{
    public WelcomeView()
    {
        InitializeComponent();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private async void Connection_Click(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is not null && (sender as FrameworkElement)?.DataContext is ConnectionProfile profile)
        {
            await ViewModel.ConnectAsync(profile);
        }
    }

    private void OpenConnectionMenu_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null && ResolveProfile(sender) is { } profile)
        {
            _ = ViewModel.ConnectAsync(profile);
        }
    }

    private void DeleteConnection_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null && ResolveProfile(sender) is { } profile)
        {
            ViewModel.DeleteConnection(profile);
        }
    }

    private void NewConnection_Click(object sender, RoutedEventArgs e) => ViewModel?.RequestConnectionDialog();

    private void OpenSqlite_Click(object sender, RoutedEventArgs e) => ViewModel?.RequestOpenSqliteFile();

    private void Demo_Click(object sender, RoutedEventArgs e)
    {
        var demo = ViewModel?.AllConnections.FirstOrDefault(profile => profile.IsDemo);
        if (demo is not null)
        {
            _ = ViewModel!.ConnectAsync(demo);
        }
    }

    private static ConnectionProfile? ResolveProfile(object sender)
    {
        if (sender is MenuItem item)
        {
            if (item.DataContext is ConnectionProfile profile)
            {
                return profile;
            }

            if (item.Parent is ContextMenu { PlacementTarget: FrameworkElement { DataContext: ConnectionProfile placed } })
            {
                return placed;
            }
        }

        return null;
    }
}
