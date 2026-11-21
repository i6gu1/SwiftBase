using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using SwiftBase.Models;
using SwiftBase.ViewModels;
using SwiftBase.Views;

namespace SwiftBase.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private IInputElement? _focusBeforePalette;
    private bool _closePromptDone;
    private bool _shutdownCleanupDone;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        _viewModel.ConnectionDialogRequested += OpenConnectionDialog;
        _viewModel.OpenSqliteFileRequested += OpenSqliteFileDialog;
        _viewModel.PreviewSqlRequested += ShowSqlPreview;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(WindowProc);
        }
    }

    private const int WmGetMinMaxInfo = 0x0024;
    private const int MonitorDefaultToNearest = 0x00000002;

    private static IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmGetMinMaxInfo)
        {
            return IntPtr.Zero;
        }

        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            return IntPtr.Zero;
        }

        // WindowChrome otherwise maximizes against the full monitor and the
        // bottom of the app (status bar, pagination) ends up under the taskbar.
        var mmi = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        mmi.MaxPosition.X = info.WorkArea.Left - info.MonitorArea.Left;
        mmi.MaxPosition.Y = info.WorkArea.Top - info.MonitorArea.Top;
        mmi.MaxSize.X = info.WorkArea.Right - info.WorkArea.Left;
        mmi.MaxSize.Y = info.WorkArea.Bottom - info.WorkArea.Top;
        Marshal.StructureToPtr(mmi, lParam, true);
        handled = true;
        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect MonitorArea;
        public NativeRect WorkArea;
        public int Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.IsPaletteOpen))
        {
            return;
        }

        if (_viewModel.IsPaletteOpen)
        {
            _focusBeforePalette = Keyboard.FocusedElement;
            Dispatcher.BeginInvoke(() =>
            {
                PaletteSearchBox.Focus();
                Keyboard.Focus(PaletteSearchBox);
                PaletteSearchBox.SelectAll();
            }, DispatcherPriority.Input);
            return;
        }

        var previous = _focusBeforePalette;
        _focusBeforePalette = null;
        if (previous is UIElement { IsVisible: true } element)
        {
            Dispatcher.BeginInvoke(() => Keyboard.Focus(element), DispatcherPriority.Input);
        }
    }

    // ----------------------------------------------------------------- title bar

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
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

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize()
    {
        if (WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(this);
        }
        else
        {
            SystemCommands.MaximizeWindow(this);
        }
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (MaximizeButton is not null)
        {
            MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
            MaximizeButton.ToolTip = WindowState == WindowState.Maximized ? "Restore" : "Maximize";
        }

        if (WindowBorder is not null)
        {
            WindowBorder.BorderThickness = WindowState == WindowState.Maximized ? new Thickness(0) : new Thickness(1);
        }
    }

    // ----------------------------------------------------------------- palette

    private void Palette_Click(object sender, RoutedEventArgs e) => _viewModel.OpenPalette(PaletteMode.Commands);

    private void PaletteSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                _viewModel.ClosePalette();
                e.Handled = true;
                break;

            case Key.Enter:
                _ = ExecutePaletteItemAsync(_viewModel.SelectedPaletteItem);
                e.Handled = true;
                break;

            case Key.Down:
                _viewModel.MovePaletteSelection(1);
                ScrollPaletteSelectionIntoView();
                e.Handled = true;
                break;

            case Key.Up:
                _viewModel.MovePaletteSelection(-1);
                ScrollPaletteSelectionIntoView();
                e.Handled = true;
                break;
        }
    }

    private void ScrollPaletteSelectionIntoView()
    {
        if (_viewModel.SelectedPaletteItem is { } item)
        {
            PaletteList.ScrollIntoView(item);
        }
    }

    private async System.Threading.Tasks.Task ExecutePaletteItemAsync(PaletteItem? item)
    {
        if (item is null)
        {
            return;
        }

        await _viewModel.ExecutePaletteItemAsync(item);
    }

    private async void PaletteList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is not { DataContext: PaletteItem item })
        {
            return;
        }

        _viewModel.SelectedPaletteItem = item;
        e.Handled = true;
        await ExecutePaletteItemAsync(item);
    }

    private void PaletteBackdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, sender))
        {
            _viewModel.ClosePalette();
        }
    }

    // ----------------------------------------------------------------- shortcuts

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

        if (ctrl && e.Key == Key.P)
        {
            if (_viewModel.IsPaletteOpen && _viewModel.PaletteMode == PaletteMode.Commands)
            {
                _viewModel.ClosePalette();
            }
            else
            {
                _viewModel.OpenPalette(PaletteMode.Commands);
            }

            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.K)
        {
            if (_viewModel.IsPaletteOpen && _viewModel.PaletteMode == PaletteMode.Connections)
            {
                _viewModel.ClosePalette();
            }
            else
            {
                _viewModel.OpenPalette(PaletteMode.Connections);
            }

            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && _viewModel.IsPaletteOpen)
        {
            _viewModel.ClosePalette();
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.S)
        {
            SaveCurrentTab();
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.W)
        {
            if (_viewModel.SelectedTab is { } tab)
            {
                _viewModel.CloseTab(tab);
            }

            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.T)
        {
            if (_viewModel.IsConnected)
            {
                _viewModel.NewQueryTab();
            }
            else
            {
                _viewModel.ShowToast("Connect to a database first.", true);
            }

            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.N)
        {
            AddStagedRow();
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.Tab)
        {
            CycleTabs(shift ? -1 : 1);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F5 && _viewModel.IsConnected)
        {
            RefreshCurrent();
            e.Handled = true;
        }
    }

    private void SaveCurrentTab()
    {
        if (_viewModel.CanSaveSelected)
        {
            _viewModel.SaveSelected();
            return;
        }

        if (_viewModel.SelectedTableTab is not null)
        {
            _viewModel.ShowToast("No pending changes to save.");
        }
        else if (_viewModel.IsConnected)
        {
            _viewModel.ShowToast("Open a table tab to save changes.", true);
        }
    }

    private void AddStagedRow()
    {
        if (!_viewModel.IsConnected)
        {
            OpenConnectionDialog();
            return;
        }

        if (_viewModel.SelectedTableTab is { } table && table.AddRowCommand.CanExecute(null))
        {
            table.AddRowCommand.Execute(null);
        }
        else
        {
            _viewModel.ShowToast("Open a writable table tab to stage a new row.", true);
        }
    }

    private void RefreshCurrent()
    {
        if (_viewModel.SelectedTableTab is { } table && table.RefreshCommand.CanExecute(null))
        {
            table.RefreshCommand.Execute(null);
            return;
        }

        _ = _viewModel.RefreshSchemaAsync();
    }

    private void CycleTabs(int delta)
    {
        if (_viewModel.Tabs.Count == 0)
        {
            return;
        }

        var index = _viewModel.SelectedTab is null ? -1 : _viewModel.Tabs.IndexOf(_viewModel.SelectedTab);
        index = (index + delta + _viewModel.Tabs.Count) % _viewModel.Tabs.Count;
        _viewModel.SelectedTab = _viewModel.Tabs[index];
    }

    // ----------------------------------------------------------------- dialogs

    private void OpenConnectionDialog()
    {
        if (_viewModel.IsConnecting)
        {
            return;
        }

        var editor = new ConnectionEditorViewModel();
        var dialog = new ConnectionDialog(editor) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _ = _viewModel.RegisterConnectionAsync(editor.CreateConfig());
        }
    }

    private void OpenSqliteFileDialog()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "SQLite databases (*.db;*.sqlite;*.sqlite3)|*.db;*.sqlite;*.sqlite3|All files (*.*)|*.*",
            CheckFileExists = true,
            Title = "Open a SQLite database",
        };

        if (dialog.ShowDialog(this) == true)
        {
            _ = _viewModel.OpenSqliteFileAsync(dialog.FileName);
        }
    }

    private void ShowSqlPreview(string sql)
    {
        var window = new SqlPreviewWindow(sql) { Owner = this };
        window.ShowDialog();
    }

    // ----------------------------------------------------------------- drag & drop

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = TryGetDroppedSqliteFile(e.Data, out _) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (TryGetDroppedSqliteFile(e.Data, out var path))
        {
            await _viewModel.OpenSqliteFileAsync(path);
        }
    }

    private static bool TryGetDroppedSqliteFile(IDataObject data, out string path)
    {
        path = "";
        if (!data.GetDataPresent(DataFormats.FileDrop) || data.GetData(DataFormats.FileDrop) is not string[] files)
        {
            return false;
        }

        var file = files.FirstOrDefault(IsSqlitePath);
        if (file is not null)
        {
            path = file;
            return true;
        }

        return false;
    }

    private static bool IsSqlitePath(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".db", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".sqlite", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".sqlite3", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".db3", StringComparison.OrdinalIgnoreCase);
    }

    // ----------------------------------------------------------------- lifecycle

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_closePromptDone)
        {
            _closePromptDone = true;
            if (_viewModel.HasPendingChanges)
            {
                var answer = MessageBox.Show(
                    this,
                    "There are staged changes that have not been saved.\n\nClose SwiftBase and discard them?",
                    "SwiftBase",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);

                if (answer != MessageBoxResult.Yes)
                {
                    _closePromptDone = false;
                    e.Cancel = true;
                    return;
                }
            }
        }

        if (!_shutdownCleanupDone)
        {
            _shutdownCleanupDone = true;
            try
            {
                _viewModel.DisconnectForShutdown();
            }
            catch
            {
                // Never block shutdown because cleanup failed.
            }
        }

        _viewModel.ConnectionDialogRequested -= OpenConnectionDialog;
        _viewModel.OpenSqliteFileRequested -= OpenSqliteFileDialog;
        _viewModel.PreviewSqlRequested -= ShowSqlPreview;
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        base.OnClosing(e);
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
