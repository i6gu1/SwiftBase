using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using SwiftBase.Core.Models;
using SwiftBase.ViewModels;

namespace SwiftBase.Views;

public partial class ConnectionDialog : Window
{
    public ConnectionDialog(ConnectionEditorViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // The design height is 730 DIP, which does not fit 720p / 150% setups.
        // Keep the title bar and footer visible and let the form scroll.
        var workArea = SystemParameters.WorkArea;
        Width = Math.Min(Width, Math.Max(520, workArea.Width - 40));
        Height = Math.Min(Height, Math.Max(420, workArea.Height - 40));

        Loaded += (_, _) =>
        {
            SyncPasswordBoxes();
            UrlBox.Focus();
        };
    }

    private ConnectionEditorViewModel? ViewModel => DataContext as ConnectionEditorViewModel;

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // The mouse was released before the drag actually started.
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.Validate() == true)
        {
            DialogResult = true;
        }
    }

    private void Engine_Checked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || sender is not RadioButton { Tag: string tag })
        {
            return;
        }

        if (Enum.TryParse<DatabaseEngine>(tag, true, out var engine))
        {
            ViewModel.Engine = engine;
        }
    }

    private void ImportUrl_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.ImportUrl();
        SyncPasswordBoxes();
    }

    private void BrowseSqlite_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = "SQLite databases (*.db;*.sqlite;*.sqlite3)|*.db;*.sqlite;*.sqlite3|All files (*.*)|*.*",
            CheckFileExists = false,
            Title = "Choose a SQLite database",
        };

        if (dialog.ShowDialog(this) == true)
        {
            ViewModel.Database = dialog.FileName;
            if (string.IsNullOrWhiteSpace(ViewModel.Name))
            {
                ViewModel.Name = Path.GetFileNameWithoutExtension(dialog.FileName);
            }
        }
    }

    private void BrowseKey_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = "Private keys (*.*)|*.*",
            CheckFileExists = true,
            Title = "Choose an SSH private key",
        };

        if (dialog.ShowDialog(this) == true)
        {
            ViewModel.SshKeyPath = dialog.FileName;
        }
    }

    private async void TestButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
        {
            await ViewModel.TestConnectionAsync();
        }
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
        {
            ViewModel.Password = NewPasswordBox.Password;
        }
    }

    private void SshPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
        {
            ViewModel.SshPassword = SshPasswordBox.Password;
        }
    }

    private void SshPassphraseBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
        {
            ViewModel.SshPassphrase = SshPassphraseBox.Password;
        }
    }

    private void SyncPasswordBoxes()
    {
        if (ViewModel is null)
        {
            return;
        }

        if (NewPasswordBox.Password != ViewModel.Password)
        {
            NewPasswordBox.Password = ViewModel.Password;
        }

        if (SshPasswordBox.Password != ViewModel.SshPassword)
        {
            SshPasswordBox.Password = ViewModel.SshPassword;
        }

        if (SshPassphraseBox.Password != ViewModel.SshPassphrase)
        {
            SshPassphraseBox.Password = ViewModel.SshPassphrase;
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
