using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SwiftBase.Models;
using SwiftBase.ViewModels;

namespace SwiftBase.Views;

public partial class WorkspaceView : UserControl
{
    public WorkspaceView()
    {
        InitializeComponent();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void Table_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null && (sender as FrameworkElement)?.DataContext is DbTable table)
        {
            ViewModel.OpenTable(table);
        }
    }

    private void NewQuery_Click(object sender, RoutedEventArgs e) => ViewModel?.NewQueryTab();

    private void SwitchConnection_Click(object sender, RoutedEventArgs e) => ViewModel?.OpenPalette(PaletteMode.Connections);

    private void TabStrip_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle)
        {
            return;
        }

        var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item?.DataContext is TabViewModel tab)
        {
            ViewModel?.CloseTab(tab);
            e.Handled = true;
        }
    }

    private static T? FindAncestor<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element is not null)
        {
            if (element is T target)
            {
                return target;
            }

            element = VisualTreeHelper.GetParent(element);
        }

        return null;
    }
}
