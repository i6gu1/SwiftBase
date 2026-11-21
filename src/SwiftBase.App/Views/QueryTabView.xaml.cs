using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using SwiftBase.ViewModels;

namespace SwiftBase.Views;

public partial class QueryTabView : UserControl
{
    private QueryTabViewModel? _viewModel;
    private bool _focused;

    public QueryTabView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => AttachViewModel();
        Loaded += (_, _) =>
        {
            if (!_focused)
            {
                _focused = true;
                SqlEditor.FocusEditor();
            }
        };

        SqlEditor.SelectionChanged += (_, _) =>
        {
            if (_viewModel is not null)
            {
                _viewModel.SelectionText = SqlEditor.SelectedText;
            }
        };
    }

    private void AttachViewModel()
    {
        _viewModel = DataContext as QueryTabViewModel;
        if (_viewModel is null)
        {
            return;
        }

        SqlEditor.Completions = _viewModel.Completions;
    }

    private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            RunCurrentSql();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _viewModel.IsRunning)
        {
            _viewModel.CancelCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void RunCurrentSql()
    {
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.SelectionText = SqlEditor.SelectedText;
        if (_viewModel.RunCommand.CanExecute(null))
        {
            _viewModel.RunCommand.Execute(null);
        }
    }

    private void ResultGrid_AutoGeneratingColumn(object? sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        var converter = TryFindResource("QueryCell") as IValueConverter;
        var monoFont = TryFindResource("MonoFont") as FontFamily ?? new FontFamily("Consolas");
        var textBrush = TryFindResource("TextBrush") as Brush ?? Brushes.White;

        var factory = new FrameworkElementFactory(typeof(TextBlock));
        factory.SetBinding(TextBlock.TextProperty, new Binding($"[{e.PropertyName}]")
        {
            Converter = converter,
            Mode = BindingMode.OneWay,
        });
        factory.SetBinding(ToolTipProperty, new Binding($"[{e.PropertyName}]")
        {
            Converter = converter,
            Mode = BindingMode.OneWay,
        });
        factory.SetValue(TextBlock.FontFamilyProperty, monoFont);
        factory.SetValue(TextBlock.FontSizeProperty, 11.5d);
        factory.SetValue(TextBlock.ForegroundProperty, textBrush);
        factory.SetValue(MarginProperty, new Thickness(10, 0, 10, 0));
        factory.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        factory.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);

        var name = (e.PropertyName ?? "").ToLowerInvariant();
        var width = name.Contains("json") ? 280d
            : name.Contains("email") ? 230d
            : name.Contains("name") ? 190d
            : name.Contains("id") ? 140d
            : 165d;

        e.Column = new DataGridTemplateColumn
        {
            Header = e.PropertyName,
            CellTemplate = new DataTemplate { VisualTree = factory },
            Width = new DataGridLength(width),
            SortMemberPath = e.PropertyName,
            IsReadOnly = true,
        };
    }

    private void ExportCsv_Click(object sender, RoutedEventArgs e) =>
        Export("CSV files (*.csv)|*.csv|All files (*.*)|*.*", ".csv", vm => vm.BuildCsv());

    private void ExportJson_Click(object sender, RoutedEventArgs e) =>
        Export("JSON files (*.json)|*.json|All files (*.*)|*.*", ".json", vm => vm.BuildJson());

    private void ExportInserts_Click(object sender, RoutedEventArgs e) =>
        Export("SQL files (*.sql)|*.sql|All files (*.*)|*.*", ".sql", vm => vm.BuildInserts());

    private void Export(string filter, string extension, Func<QueryTabViewModel, string> build)
    {
        if (_viewModel is null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = filter,
            DefaultExt = extension,
            FileName = $"query-result{extension}",
            AddExtension = true,
        };

        var owner = Window.GetWindow(this);
        var result = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
        if (result != true)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, build(_viewModel), new UTF8Encoding(true));
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner ?? Application.Current.MainWindow,
                "Could not export the result:\n" + ex.GetBaseException().Message,
                "SwiftBase export",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
