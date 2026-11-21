using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using SwiftBase.Models;

namespace SwiftBase.Converters;

/// <summary>
/// Resolves a single cell from a <see cref="RowViewModel"/> using the column
/// name passed in <see cref="IValueConverter.Convert"/>'s parameter. Binding
/// directly to the row's overloaded indexer is ambiguous in WPF, and path
/// indexers break on column names that contain brackets.
/// </summary>
public sealed class CellValueConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is RowViewModel row && parameter is string column
            && row.Cells.TryGetValue(column, out var cell)
            ? cell.DisplayValue
            : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class CellBackgroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is RowViewModel row && parameter is string column
            && row.Cells.TryGetValue(column, out var cell)
            ? cell.Background
            : Brushes.Transparent;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class CellForegroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is RowViewModel row && parameter is string column
            && row.Cells.TryGetValue(column, out var cell)
            ? cell.Foreground
            : Brushes.White;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class CellFontStyleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is RowViewModel row && parameter is string column
            && row.Cells.TryGetValue(column, out var cell)
            ? cell.FontStyle
            : FontStyles.Normal;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class CellTextDecorationsConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is RowViewModel row && parameter is string column
            && row.Cells.TryGetValue(column, out var cell)
            ? cell.TextDecorations
            : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
