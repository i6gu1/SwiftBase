using System.Windows.Media;
using SwiftBase.ViewModels;

namespace SwiftBase.Models;

/// <summary>A row in a table, with staging state (new / edited / marked deleted).</summary>
public sealed class RowViewModel : ObservableObject
{
    private static readonly Brush NewBrush = Frozen("#10281B");
    private static readonly Brush DeletedBrush = Frozen("#2A1418");
    private static readonly Brush NormalBrush = Brushes.Transparent;

    private bool _isNew;
    private bool _isDeleted;

    public RowViewModel(IEnumerable<KeyValuePair<string, string?>> values)
    {
        foreach (var pair in values)
        {
            var cell = new CellViewModel(pair.Key, pair.Value, this);
            Cells[pair.Key] = cell;
            OrderedCells.Add(cell);
        }
    }

    public Dictionary<string, CellViewModel> Cells { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<CellViewModel> OrderedCells { get; } = new();

    public CellViewModel this[string column] =>
        Cells.TryGetValue(column, out var cell) ? cell : new CellViewModel(column, "", this);

    public CellViewModel this[int index] =>
        index >= 0 && index < OrderedCells.Count ? OrderedCells[index] : OrderedCells.FirstOrDefault() ?? new CellViewModel("", "", this);

    public event Action? Changed;

    public bool IsNew
    {
        get => _isNew;
        private set
        {
            if (!Set(ref _isNew, value))
            {
                return;
            }

            RefreshCells();
            OnPropertyChanged(nameof(Background));
            OnPropertyChanged(nameof(HasChanges));
            Changed?.Invoke();
        }
    }

    public bool IsDeleted
    {
        get => _isDeleted;
        private set
        {
            if (!Set(ref _isDeleted, value))
            {
                return;
            }

            RefreshCells();
            OnPropertyChanged(nameof(Background));
            OnPropertyChanged(nameof(HasChanges));
            Changed?.Invoke();
        }
    }

    public Brush Background => IsDeleted ? DeletedBrush : IsNew ? NewBrush : NormalBrush;

    public bool HasChanges => IsNew || IsDeleted || Cells.Values.Any(c => c.IsDirty);

    public bool IsDirty => Cells.Values.Any(c => c.IsDirty);

    public Dictionary<string, string?> Snapshot()
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in OrderedCells)
        {
            values[cell.Column] = cell.IsNull ? null : cell.Value;
        }

        return values;
    }

    public Dictionary<string, string?> OriginalSnapshot()
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in OrderedCells)
        {
            values[cell.Column] = cell.OriginalValue;
        }

        return values;
    }

    public Dictionary<string, string?> ChangedSnapshot()
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in OrderedCells.Where(c => c.IsDirty))
        {
            values[cell.Column] = cell.IsNull ? null : cell.Value;
        }

        return values;
    }

    public void MarkNew()
    {
        IsNew = true;
        foreach (var cell in Cells.Values)
        {
            cell.RefreshVisual();
        }
    }

    public void MarkDeleted()
    {
        if (IsNew)
        {
            return;
        }

        IsDeleted = true;
    }

    public void Restore()
    {
        IsDeleted = false;
        foreach (var cell in OrderedCells)
        {
            cell.RefreshVisual();
        }
    }

    public void RevertAll()
    {
        foreach (var cell in OrderedCells)
        {
            cell.Revert();
        }

        _isDeleted = false;
        OnPropertyChanged(nameof(IsDeleted));
        OnPropertyChanged(nameof(Background));
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(IsDirty));
        RefreshCells();
    }

    public void CommitAll()
    {
        foreach (var cell in Cells.Values)
        {
            cell.Commit();
        }

        _isNew = false;
        _isDeleted = false;
        OnPropertyChanged(nameof(IsNew));
        OnPropertyChanged(nameof(IsDeleted));
        OnPropertyChanged(nameof(Background));
        OnPropertyChanged(nameof(HasChanges));
        RefreshCells();
    }

    public void NotifyCellChanged()
    {
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(IsDirty));
        // Empty property name tells WPF to refresh every binding on this row,
        // which is how the per-cell converters stay in sync.
        OnPropertyChanged(string.Empty);
        Changed?.Invoke();
    }

    private void RefreshCells()
    {
        foreach (var cell in Cells.Values)
        {
            cell.RefreshVisual();
        }

        OnPropertyChanged(string.Empty);
    }

    private static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
