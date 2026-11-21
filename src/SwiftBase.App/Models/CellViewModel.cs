using System.Windows;
using System.Windows.Media;
using SwiftBase.ViewModels;

namespace SwiftBase.Models;

/// <summary>A single editable value inside a grid row. NULL is distinct from "".</summary>
public sealed class CellViewModel : ObservableObject
{
    private static readonly Brush DirtyBrush = Frozen("#3A2E14");
    private static readonly Brush NullBrush = Frozen("#101A27");
    private static readonly Brush NormalBrush = Brushes.Transparent;
    private static readonly Brush TextBrush = Frozen("#DCE7F5");
    private static readonly Brush NullTextBrush = Frozen("#64748B");
    private static readonly Brush DeletedTextBrush = Frozen("#F08A8A");
    private static readonly TextDecorationCollection StrikeDecorations = System.Windows.TextDecorations.Strikethrough;

    private string _value;
    private bool _isNull;

    public CellViewModel(string column, string? value, RowViewModel owner)
    {
        Column = column;
        _value = value ?? "";
        _isNull = value is null;
        OriginalValue = value;
        Owner = owner;
    }

    public string Column { get; }
    public RowViewModel Owner { get; }
    public string? OriginalValue { get; private set; }

    public string Value
    {
        get => _value;
        set
        {
            value ??= "";
            if (_value == value && !_isNull)
            {
                return;
            }

            _value = value;
            _isNull = false;
            IsDirty = !string.Equals(_value, OriginalValue, StringComparison.Ordinal);
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDirty));
            RefreshVisual();
            Owner.NotifyCellChanged();
        }
    }

    public bool IsDirty { get; private set; }
    public bool IsNull => _isNull;
    public string DisplayValue => _isNull ? "NULL" : _value;

    public Brush Background =>
        Owner.IsNew || Owner.IsDeleted
            ? NormalBrush
            : IsDirty
                ? DirtyBrush
                : _isNull
                    ? NullBrush
                    : NormalBrush;

    public Brush Foreground =>
        Owner.IsDeleted ? DeletedTextBrush : _isNull ? NullTextBrush : TextBrush;

    public FontStyle FontStyle => _isNull ? FontStyles.Italic : FontStyles.Normal;

    public TextDecorationCollection? TextDecorations => Owner.IsDeleted ? StrikeDecorations : null;

    public void SetNull()
    {
        if (_isNull)
        {
            return;
        }

        _isNull = true;
        _value = "";
        IsDirty = !(OriginalValue is null);
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(DisplayValue));
        RefreshVisual();
        Owner.NotifyCellChanged();
    }

    public void Revert()
    {
        _isNull = OriginalValue is null;
        _value = OriginalValue ?? "";
        IsDirty = false;
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(IsDirty));
        RefreshVisual();
        Owner.NotifyCellChanged();
    }

    public void Commit()
    {
        OriginalValue = _isNull ? null : _value;
        if (!IsDirty)
        {
            return;
        }

        IsDirty = false;
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(Background));
    }

    public void RefreshVisual()
    {
        OnPropertyChanged(nameof(DisplayValue));
        OnPropertyChanged(nameof(Background));
        OnPropertyChanged(nameof(Foreground));
        OnPropertyChanged(nameof(FontStyle));
        OnPropertyChanged(nameof(TextDecorations));
    }

    private static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
