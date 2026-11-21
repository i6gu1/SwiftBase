using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SwiftBase.Converters;
using SwiftBase.Models;
using SwiftBase.ViewModels;

namespace SwiftBase.Views;

public partial class TableTabView : UserControl
{
    private TableTabViewModel? _viewModel;

    public TableTabView()
    {
        InitializeComponent();

        // The implicit row style is shared with the structure grid (whose
        // items have no Background property), so the data grid gets the
        // per-row staging background explicitly.
        if (TryFindResource(typeof(DataGridRow)) is Style baseRowStyle)
        {
            var rowStyle = new Style(typeof(DataGridRow), baseRowStyle);
            rowStyle.Setters.Add(new Setter(BackgroundProperty, new Binding("Background")));
            Grid.ItemContainerStyle = rowStyle;
        }

        DataContextChanged += (_, _) => AttachViewModel();
        Loaded += (_, _) =>
        {
            if (_viewModel is null)
            {
                AttachViewModel();
            }
        };
    }

    private void AttachViewModel()
    {
        var next = DataContext as TableTabViewModel;
        if (ReferenceEquals(next, _viewModel))
        {
            return;
        }

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }

        _viewModel = next;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        }

        BuildColumns();
        UpdateSortIndicators();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TableTabViewModel.SortColumn) or nameof(TableTabViewModel.SortDescending))
        {
            UpdateSortIndicators();
        }
        else if (e.PropertyName == nameof(TableTabViewModel.IsInspectorOpen) && _viewModel?.IsInspectorOpen == true)
        {
            Dispatcher.BeginInvoke(() => InspectorEditor.FocusEditor(), DispatcherPriority.Input);
        }
    }

    private void BuildColumns()
    {
        Grid.Columns.Clear();
        if (_viewModel is null)
        {
            return;
        }

        var monoFont = TryFindResource("MonoFont") as FontFamily ?? new FontFamily("Consolas");
        var textBrush = TryFindResource("TextBrush") as Brush ?? Brushes.White;
        var dimBrush = TryFindResource("DimBrush") as Brush ?? Brushes.Gray;
        var yellowBrush = TryFindResource("YellowBrush") as Brush ?? Brushes.Gold;
        var cellBase = TryFindResource(typeof(DataGridCell)) as Style;

        for (var index = 0; index < _viewModel.Columns.Count; index++)
        {
            var column = _viewModel.Columns[index];

            var header = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 4),
            };

            var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
            nameRow.Children.Add(new TextBlock
            {
                Text = column.Name,
                FontFamily = monoFont,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = textBrush,
                VerticalAlignment = VerticalAlignment.Center,
            });

            if (column.IsPrimaryKey)
            {
                nameRow.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x21, 0x11)),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(4, 0, 4, 0),
                    Margin = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = "PK",
                        FontSize = 8,
                        FontWeight = FontWeights.Bold,
                        Foreground = yellowBrush,
                    },
                });
            }

            header.Children.Add(nameRow);
            header.Children.Add(new TextBlock
            {
                Text = column.DataType,
                FontSize = 9,
                Foreground = dimBrush,
                Margin = new Thickness(0, 1, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });

            header.ToolTip = $"{column.Name} · {column.DataType}"
                             + (column.IsPrimaryKey ? " · primary key" : "")
                             + (column.IsNullable ? " · nullable" : " · not null");

            var cellStyle = new Style(typeof(DataGridCell), cellBase);
            cellStyle.Setters.Add(new Setter(BackgroundProperty, CellBinding(new CellBackgroundConverter(), column.Name)));

            var factory = new FrameworkElementFactory(typeof(TextBlock));
            factory.SetBinding(TextBlock.TextProperty, CellBinding(new CellValueConverter(), column.Name));
            factory.SetBinding(TextBlock.ForegroundProperty, CellBinding(new CellForegroundConverter(), column.Name));
            factory.SetBinding(TextBlock.FontStyleProperty, CellBinding(new CellFontStyleConverter(), column.Name));
            factory.SetBinding(TextBlock.TextDecorationsProperty, CellBinding(new CellTextDecorationsConverter(), column.Name));
            factory.SetBinding(ToolTipProperty, CellBinding(new CellValueConverter(), column.Name));
            factory.SetValue(TextBlock.FontFamilyProperty, monoFont);
            factory.SetValue(TextBlock.FontSizeProperty, 11.5d);
            factory.SetValue(MarginProperty, new Thickness(10, 0, 10, 0));
            factory.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            factory.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);

            var dataColumn = new DataGridTemplateColumn
            {
                Header = header,
                CellTemplate = new DataTemplate { VisualTree = factory },
                CellStyle = cellStyle,
                SortMemberPath = column.Name,
                CanUserSort = true,
                IsReadOnly = true,
                Width = new DataGridLength(GetColumnWidth(column)),
            };

            Grid.Columns.Add(dataColumn);
        }
    }

    private static Binding CellBinding(IValueConverter converter, string column) => new()
    {
        Converter = converter,
        ConverterParameter = column,
        Mode = BindingMode.OneWay,
    };

    private static double GetColumnWidth(DbColumn column)
    {
        var type = column.DataType.ToLowerInvariant();
        if (type.Contains("json"))
        {
            return 300;
        }

        if (column.IsPrimaryKey)
        {
            return 165;
        }

        if (type.Contains("timestamp") || type.Contains("datetime") || type.Contains("date") || type.Contains("time"))
        {
            return 180;
        }

        if (type.Contains("bool"))
        {
            return 100;
        }

        if (type.Contains("int") || type.Contains("numeric") || type.Contains("decimal")
            || type.Contains("double") || type.Contains("real") || type.Contains("float") || type.Contains("money"))
        {
            return 125;
        }

        if (type.Contains("text") || type.Contains("char") || type.Contains("uuid"))
        {
            return 235;
        }

        return 170;
    }

    private void UpdateSortIndicators()
    {
        foreach (var column in Grid.Columns)
        {
            column.SortDirection = _viewModel is not null
                                   && string.Equals(column.SortMemberPath, _viewModel.SortColumn, StringComparison.OrdinalIgnoreCase)
                ? _viewModel.SortDescending ? ListSortDirection.Descending : ListSortDirection.Ascending
                : null;
        }
    }

    private void Grid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        if (_viewModel is not null && !string.IsNullOrWhiteSpace(e.Column.SortMemberPath))
        {
            _viewModel.SortBy(e.Column.SortMemberPath);
        }
    }

    private void Grid_CurrentCellChanged(object sender, EventArgs e)
    {
        if (_viewModel is null || !Grid.CurrentCell.IsValid)
        {
            return;
        }

        var column = ResolveColumn(Grid.CurrentCell.Column);
        if (column is null || Grid.CurrentCell.Item is not RowViewModel row)
        {
            return;
        }

        _viewModel.SetCurrentCell(row[column.Name], column);
        if (_viewModel.IsInspectorOpen)
        {
            _viewModel.OpenInspector();
        }
    }

    private DbColumn? ResolveColumn(DataGridColumn? dataGridColumn)
    {
        if (_viewModel is null || dataGridColumn?.SortMemberPath is not { Length: > 0 } name)
        {
            return null;
        }

        return _viewModel.Columns.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private bool TryGetCurrentCell(out CellViewModel? cell, out DbColumn? column)
    {
        cell = null;
        column = null;
        if (_viewModel is null || !Grid.CurrentCell.IsValid || Grid.CurrentCell.Item is not RowViewModel row)
        {
            return false;
        }

        column = ResolveColumn(Grid.CurrentCell.Column);
        if (column is null)
        {
            return false;
        }

        cell = row[column.Name];
        _viewModel.SetCurrentCell(cell, column);
        return true;
    }

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || _viewModel is null)
        {
            return;
        }

        if (FindAncestor<DataGridCell>(e.OriginalSource as DependencyObject) is null)
        {
            return;
        }

        _viewModel.OpenInspector();
        e.Handled = true;
    }

    private void Grid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var cell = FindAncestor<DataGridCell>(e.OriginalSource as DependencyObject);
        if (cell is null)
        {
            return;
        }

        if (cell.DataContext is RowViewModel row)
        {
            Grid.SelectedItem = row;
        }

        Grid.CurrentCell = new DataGridCellInfo(cell.DataContext, cell.Column);
        cell.Focus();
    }

    private void Grid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Space:
            case Key.Enter:
                _viewModel.OpenInspector();
                e.Handled = true;
                break;

            case Key.Delete when _viewModel.CanEdit:
                if (_viewModel.DeleteRowCommand.CanExecute(null))
                {
                    _viewModel.DeleteRowCommand.Execute(null);
                }

                e.Handled = true;
                break;
        }
    }

    private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _viewModel.IsFilterOpen = true;
            FilterValueBox.Focus();
            FilterValueBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (_viewModel.IsInspectorOpen)
            {
                _viewModel.CloseInspectorCommand.Execute(null);
                e.Handled = true;
            }
            else if (_viewModel.IsFilterOpen)
            {
                _viewModel.IsFilterOpen = false;
                e.Handled = true;
            }
        }
    }

    private void FilterValue_KeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        if (e.Key == Key.Enter && _viewModel.ApplyFilterCommand.CanExecute(null))
        {
            _viewModel.ApplyFilterCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _viewModel.IsFilterOpen = false;
            e.Handled = true;
        }
    }

    private void Inspector_Click(object sender, RoutedEventArgs e)
    {
        _viewModel?.OpenInspector();
    }

    private void CopyCell_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetCurrentCell(out var cell, out _) && cell is not null)
        {
            try
            {
                Clipboard.SetText(cell.DisplayValue);
            }
            catch (Exception)
            {
                // The clipboard can be locked by another process; ignore the copy.
            }
        }
    }

    private void RevertCell_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetCurrentCell(out var cell, out _) && cell is not null)
        {
            cell.Revert();
            if (_viewModel?.IsInspectorOpen == true)
            {
                _viewModel.RevertInspectorCommand.Execute(null);
            }
        }
    }

    private void SetNull_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null || !_viewModel.CanEdit || !TryGetCurrentCell(out var cell, out _) || cell is null)
        {
            return;
        }

        if (_viewModel.IsInspectorOpen)
        {
            _viewModel.SetInspectorNullCommand.Execute(null);
        }
        else
        {
            cell.SetNull();
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
