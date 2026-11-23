using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Input;
using SwiftBase.Core.Interfaces;
using SwiftBase.Core.Models;
using SwiftBase.Core.Providers;
using SwiftBase.Core.Services;
using SwiftBase.Models;

namespace SwiftBase.ViewModels;

/// <summary>
/// A table tab: lazy paged reads, visual filters, staging and the right inspector.
/// </summary>
public sealed class TableTabViewModel : TabViewModel
{
    public const int DefaultPageSize = 500;

    private readonly DbTable _table;
    private readonly DatabaseSession _session;
    private readonly HashSet<RowViewModel> _trackedRows = new();

    private CancellationTokenSource? _loadCts;

    private bool _isLoading;
    private string _loadError = "";
    private bool _isStructure;
    private bool _isFilterOpen;
    private bool _isFilterActive;
    private string _filterColumn = "";
    private string _filterOperator = "contains";
    private string _filterValue = "";
    private string _filterSummary = "";
    private RowViewModel? _selectedRow;
    private int _lastQueryMs;
    private int _pendingCount;
    private long _totalRows;
    private int _pageIndex;
    private int _pageCount = 1;
    private string _pageLabel = "Page 1 of 1";
    private string _rowRangeLabel = "No rows";
    private string _statusMessage = "";
    private bool _isStatusError;
    private string? _sortColumn;
    private bool _sortDescending;

    private CellViewModel? _currentCell;
    private DbColumn? _currentColumn;
    private bool _isInspectorOpen;
    private string _inspectorColumnName = "";
    private string _inspectorType = "";
    private string _inspectorRowCaption = "";
    private string _inspectorText = "";
    private bool _inspectorTextChanged;
    private bool _suppressInspectorTextTracking;
    private string _inspectorStatus = "";
    private bool _inspectorIsNull;
    private bool _inspectorIsJson;

    public TableTabViewModel(
        DbTable table,
        DatabaseSession session,
        string database,
        Action<TabViewModel> close)
        : base(close, table.Name, table.Glyph, $"{table.Schema}.{table.Name}")
    {
        _table = table;
        _session = session;
        Database = database;
        Columns = table.Columns;
        _filterColumn = Columns.FirstOrDefault()?.Name ?? "";
        CanEdit = table.Kind == TableKind.Table;
        ReadOnlyReason = table.Kind switch
        {
            TableKind.View => "Views are read-only. Open a query tab to modify the underlying tables.",
            TableKind.Function => "Functions are read-only.",
            _ => "",
        };

        ToggleFilterCommand = new RelayCommand(() => IsFilterOpen = !IsFilterOpen);
        ToggleStructureCommand = new RelayCommand(() => IsStructure = !IsStructure);
        ApplyFilterCommand = new AsyncRelayCommand(ApplyFilterAsync, () => !IsStructure);
        ClearFilterCommand = new AsyncRelayCommand(ClearFilterAsync);
        AddRowCommand = new AsyncRelayCommand(AddRowAsync, () => CanEdit && !IsLoading);
        DeleteRowCommand = new RelayCommand(DeleteSelectedRow, () => SelectedRow is not null && CanEdit && !IsLoading);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => PendingCount > 0 && CanEdit && !IsLoading);
        PreviewSqlCommand = new RelayCommand(PreviewSql, () => PendingCount > 0);
        DiscardChangesCommand = new AsyncRelayCommand(DiscardPendingAsync, () => PendingCount > 0);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsLoading);
        NextPageCommand = new AsyncRelayCommand(NextPageAsync, () => CanNextPage);
        PrevPageCommand = new AsyncRelayCommand(PreviousPageAsync, () => CanPreviousPage);
        CloseInspectorCommand = new RelayCommand(CloseInspector);
        ApplyInspectorCommand = new RelayCommand(ApplyInspector, () => IsInspectorOpen && CanEdit);
        SetInspectorNullCommand = new RelayCommand(SetInspectorNull, () => IsInspectorOpen && CanEdit);
        RevertInspectorCommand = new RelayCommand(RevertInspector, () => IsInspectorOpen);

        _ = LoadPageAsync();
    }

    public string Database { get; }
    public DbTable Table => _table;
    public IReadOnlyList<DbColumn> Columns { get; }
    public ObservableCollection<RowViewModel> Rows { get; } = new();
    public bool CanEdit { get; }
    public string ReadOnlyReason { get; }
    public bool IsReadOnly => !CanEdit;
    public bool HasNoPrimaryKey => CanEdit && !_table.HasPrimaryKey;
    public string SelectionKindLabel => "database data";

    public bool IsLoading
    {
        get => _isLoading;
        private set => Set(ref _isLoading, value);
    }

    public string LoadError
    {
        get => _loadError;
        private set
        {
            if (Set(ref _loadError, value))
            {
                OnPropertyChanged(nameof(HasLoadError));
            }
        }
    }

    public bool HasLoadError => !string.IsNullOrWhiteSpace(LoadError);

    public bool IsStructure
    {
        get => _isStructure;
        set
        {
            if (Set(ref _isStructure, value))
            {
                OnPropertyChanged(nameof(ShowGrid));
                OnPropertyChanged(nameof(ShowStructure));
                OnPropertyChanged(nameof(IsDataMode));
            }
        }
    }

    public bool ShowGrid => !IsStructure;
    public bool ShowStructure => IsStructure;

    public bool IsDataMode
    {
        get => !IsStructure;
        set => IsStructure = !value;
    }

    public bool IsFilterOpen
    {
        get => _isFilterOpen;
        set => Set(ref _isFilterOpen, value);
    }

    public bool IsFilterActive
    {
        get => _isFilterActive;
        private set => Set(ref _isFilterActive, value);
    }

    public IReadOnlyList<string> FilterColumns => Columns.Select(c => c.Name).ToList();

    public IReadOnlyList<string> FilterOperators { get; } = new[] { "contains", "equals", "starts with", "is empty", "is not empty" };

    public string FilterColumn
    {
        get => _filterColumn;
        set => Set(ref _filterColumn, value);
    }

    public string FilterOperator
    {
        get => _filterOperator;
        set => Set(ref _filterOperator, value);
    }

    public string FilterValue
    {
        get => _filterValue;
        set => Set(ref _filterValue, value);
    }

    public string FilterSummary
    {
        get => _filterSummary;
        private set => Set(ref _filterSummary, value);
    }

    public RowViewModel? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (Set(ref _selectedRow, value))
            {
                (DeleteRowCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public int LastQueryMs
    {
        get => _lastQueryMs;
        private set => Set(ref _lastQueryMs, value);
    }

    public int PendingCount
    {
        get => _pendingCount;
        private set => Set(ref _pendingCount, value);
    }

    public long TotalRows
    {
        get => _totalRows;
        private set => Set(ref _totalRows, value);
    }

    public int PageIndex
    {
        get => _pageIndex;
        private set => Set(ref _pageIndex, value);
    }

    public int PageCount
    {
        get => _pageCount;
        private set => Set(ref _pageCount, value);
    }

    public string PageLabel
    {
        get => _pageLabel;
        private set => Set(ref _pageLabel, value);
    }

    public string RowRangeLabel
    {
        get => _rowRangeLabel;
        private set => Set(ref _rowRangeLabel, value);
    }

    public string PageSizeLabel => $"{DefaultPageSize} / page";

    public string PendingLabel => PendingCount == 0 ? "No pending changes" : $"{PendingCount} pending";

    public override bool HasPendingChanges => PendingCount > 0;

    public bool CanNextPage => !IsLoading && PageIndex < PageCount - 1;
    public bool CanPreviousPage => !IsLoading && PageIndex > 0;

    public bool CanSort => !IsLoading;

    public string? SortColumn
    {
        get => _sortColumn;
        private set => Set(ref _sortColumn, value);
    }

    public bool SortDescending
    {
        get => _sortDescending;
        private set => Set(ref _sortDescending, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => Set(ref _statusMessage, value);
    }

    public bool IsStatusError
    {
        get => _isStatusError;
        private set => Set(ref _isStatusError, value);
    }

    public bool IsInspectorOpen
    {
        get => _isInspectorOpen;
        private set
        {
            if (Set(ref _isInspectorOpen, value))
            {
                RaiseInspectorCommands();
            }
        }
    }

    public string InspectorColumnName
    {
        get => _inspectorColumnName;
        private set => Set(ref _inspectorColumnName, value);
    }

    public string InspectorType
    {
        get => _inspectorType;
        private set => Set(ref _inspectorType, value);
    }

    public string InspectorRowCaption
    {
        get => _inspectorRowCaption;
        private set => Set(ref _inspectorRowCaption, value);
    }

    public string InspectorText
    {
        get => _inspectorText;
        set
        {
            if (Set(ref _inspectorText, value) && !_suppressInspectorTextTracking)
            {
                _inspectorTextChanged = true;
            }
        }
    }

    public string InspectorStatus
    {
        get => _inspectorStatus;
        private set => Set(ref _inspectorStatus, value);
    }

    public bool InspectorIsNull
    {
        get => _inspectorIsNull;
        private set => Set(ref _inspectorIsNull, value);
    }

    public bool InspectorIsJson
    {
        get => _inspectorIsJson;
        private set
        {
            if (Set(ref _inspectorIsJson, value))
            {
                OnPropertyChanged(nameof(InspectorLanguage));
            }
        }
    }

    public string InspectorLanguage => InspectorIsJson ? "json" : "text";

    public event Action<string>? PreviewRequested;

    public ICommand ToggleFilterCommand { get; }
    public ICommand ToggleStructureCommand { get; }
    public ICommand ApplyFilterCommand { get; }
    public ICommand ClearFilterCommand { get; }
    public ICommand AddRowCommand { get; }
    public ICommand DeleteRowCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand PreviewSqlCommand { get; }
    public ICommand DiscardChangesCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand PrevPageCommand { get; }
    public ICommand CloseInspectorCommand { get; }
    public ICommand ApplyInspectorCommand { get; }
    public ICommand SetInspectorNullCommand { get; }
    public ICommand RevertInspectorCommand { get; }

    public void SetCurrentCell(CellViewModel? cell, DbColumn? column)
    {
        _currentCell = cell;
        _currentColumn = column;
    }

    public void OpenInspector()
    {
        var cell = _currentCell ?? SelectedRow?.OrderedCells.FirstOrDefault();
        var column = _currentColumn
            ?? (cell is not null ? Columns.FirstOrDefault(c => string.Equals(c.Name, cell.Column, StringComparison.OrdinalIgnoreCase)) : null);

        if (cell is null || column is null)
        {
            return;
        }

        _currentCell = cell;
        _currentColumn = column;
        InspectorColumnName = column.Name;
        InspectorType = column.DataType;
        var pk = Columns.FirstOrDefault(c => c.IsPrimaryKey);
        var pkCell = pk is null ? null : SelectedRow?[pk.Name];
        InspectorRowCaption = pk is null
            ? $"row · {column.DataType}"
            : $"{pk.Name} {pkCell?.DisplayValue ?? "?"}";
        InspectorIsNull = cell.IsNull;

        var raw = cell.IsNull ? "" : cell.Value;
        InspectorIsJson = TryFormatJson(raw, out var pretty);
        SetInspectorDisplayText(InspectorIsJson ? pretty : raw);
        _inspectorTextChanged = false;
        InspectorStatus = InspectorIsJson
            ? "JSON detected · pretty printed"
            : cell.IsNull
                ? "NULL value"
                : $"{raw.Length:N0} characters";
        IsInspectorOpen = true;
    }

    public void SetInspectorText(string text)
    {
        InspectorText = text;
    }

    public void SortBy(string columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName))
        {
            return;
        }

        if (HasPendingChanges)
        {
            SetStatus("Save or discard pending changes before sorting.", true);
            return;
        }

        if (string.Equals(SortColumn, columnName, StringComparison.OrdinalIgnoreCase))
        {
            SortDescending = !SortDescending;
        }
        else
        {
            SortColumn = columnName;
            SortDescending = false;
        }

        PageIndex = 0;
        _ = LoadPageAsync();
    }

    private async Task ApplyFilterAsync()
    {
        if (HasPendingChanges)
        {
            SetStatus("Save or discard pending changes before filtering.", true);
            return;
        }

        var value = FilterValue.Trim();
        if (string.IsNullOrEmpty(value) && FilterOperator != "is empty" && FilterOperator != "is not empty")
        {
            await ClearFilterAsync();
            return;
        }

        IsFilterActive = true;
        FilterSummary = FilterOperator is "is empty" or "is not empty"
            ? $"{FilterColumn} {FilterOperator}"
            : $"{FilterColumn} {FilterOperator} \"{value}\"";
        IsFilterOpen = false;
        PageIndex = 0;

        await LoadPageAsync();
    }

    private async Task ClearFilterAsync()
    {
        if (HasPendingChanges)
        {
            SetStatus("Save or discard pending changes before clearing the filter.", true);
            return;
        }

        IsFilterActive = false;
        FilterSummary = "";
        FilterValue = "";
        IsFilterOpen = false;
        PageIndex = 0;

        await LoadPageAsync();
    }

    private async Task AddRowAsync()
    {
        if (!CanEdit)
        {
            return;
        }

        if (HasPendingChanges && IsFilterActive)
        {
            SetStatus("Save or discard pending changes before adding a row to a filtered page.", true);
            return;
        }

        if (IsFilterActive)
        {
            await ClearFilterAsync();
        }

        var values = new Dictionary<string, CellData>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in Columns)
        {
            string? display;
            if (column.IsPrimaryKey)
            {
                // Empty typed PK is skipped by the provider so the database can
                // generate it; the user can still type an explicit value.
                display = "";
            }
            else if (!string.IsNullOrWhiteSpace(column.DefaultValue) && !column.DefaultValue.Contains('('))
            {
                display = column.DefaultValue.Trim().Trim('\'');
            }
            else if (column.IsNullable)
            {
                display = null;
            }
            else
            {
                display = "";
            }

            values[column.Name] = new CellData(display, display);
        }

        var row = new RowViewModel(values);
        row.MarkNew();
        Track(row);
        SelectedRow = row;
        IsStructure = false;

        Rows.Add(row);
        TotalRows++;
        UpdatePageLabels();

        RefreshCounters();
        SetStatus("New row staged · press Ctrl+S to save");
    }

    private void DeleteSelectedRow()
    {
        if (SelectedRow is null || !CanEdit)
        {
            return;
        }

        var row = SelectedRow;
        if (row.IsNew)
        {
            Rows.Remove(row);
            Untrack(row);
            SelectedRow = null;
            TotalRows = Math.Max(0, TotalRows - 1);
            UpdatePageLabels();
        }
        else
        {
            row.MarkDeleted();
        }

        RefreshCounters();
        SetStatus(row.IsNew ? "Unsaved row removed" : "Row marked for deletion · Ctrl+S to save");
    }

    private async Task SaveAsync()
    {
        var changedRows = _trackedRows.Where(r => r.HasChanges).ToList();
        if (changedRows.Count == 0)
        {
            return;
        }

        var set = BuildChangeSet(changedRows);
        SetStatus("Saving all changes in a single transaction…");
        var result = await _session.Provider.ApplyChangesAsync(set);
        if (!result.Success)
        {
            SetStatus(result.Message, true);
            return;
        }

        foreach (var row in changedRows)
        {
            row.CommitAll();
        }

        await LoadPageAsync();
        SetStatus(result.Message);
    }

    private void PreviewSql()
    {
        var changedRows = _trackedRows.Where(r => r.HasChanges).ToList();
        if (changedRows.Count == 0)
        {
            return;
        }

        var set = BuildChangeSet(changedRows);
        var scripts = _session.Provider.BuildChangeScripts(set);
        PreviewRequested?.Invoke(string.Join(Environment.NewLine, scripts));
    }

    private async Task RefreshAsync()
    {
        if (HasPendingChanges)
        {
            SetStatus("Save or discard pending changes before refreshing.", true);
            return;
        }

        await LoadPageAsync();
        SetStatus("Refreshed");
    }

    private async Task DiscardPendingAsync()
    {
        await LoadPageAsync();
        SetStatus("Pending changes discarded");
    }

    private async Task NextPageAsync()
    {
        if (HasPendingChanges)
        {
            SetStatus("Save or discard pending changes before changing pages.", true);
            return;
        }

        if (!CanNextPage)
        {
            return;
        }

        PageIndex++;
        await LoadPageAsync();
    }

    private async Task PreviousPageAsync()
    {
        if (HasPendingChanges)
        {
            SetStatus("Save or discard pending changes before changing pages.", true);
            return;
        }

        if (!CanPreviousPage)
        {
            return;
        }

        PageIndex--;
        await LoadPageAsync();
    }

    private void CloseInspector() => IsInspectorOpen = false;

    private void SetInspectorDisplayText(string text)
    {
        _suppressInspectorTextTracking = true;
        try
        {
            InspectorText = text;
        }
        finally
        {
            _suppressInspectorTextTracking = false;
        }
    }

    private void ApplyInspector()
    {
        if (_currentCell is null || !CanEdit)
        {
            return;
        }

        if (!_inspectorTextChanged)
        {
            InspectorStatus = "No changes to apply";
            return;
        }

        _currentCell.Value = InspectorText;
        _inspectorTextChanged = false;
        InspectorIsNull = false;
        InspectorIsJson = TryFormatJson(InspectorText, out _);
        InspectorStatus = "Applied to the staging row · Ctrl+S to save";
        SetStatus($"Cell {_currentCell.Column} staged");
    }

    private void SetInspectorNull()
    {
        if (_currentCell is null || !CanEdit)
        {
            return;
        }

        _currentCell.SetNull();
        _inspectorTextChanged = false;
        SetInspectorDisplayText("");
        InspectorIsNull = true;
        InspectorIsJson = false;
        InspectorStatus = "Set to NULL · Ctrl+S to save";
        SetStatus($"Cell {_currentCell.Column} set to NULL");
    }

    private void RevertInspector()
    {
        _currentCell?.Revert();
        ReloadInspector();
        if (_currentCell is not null)
        {
            InspectorStatus = "Reverted to stored value";
        }
    }

    private void ReloadInspector()
    {
        if (_currentCell is null)
        {
            return;
        }

        var raw = _currentCell.IsNull ? "" : _currentCell.Value;
        InspectorIsJson = TryFormatJson(raw, out var pretty);
        SetInspectorDisplayText(InspectorIsJson ? pretty : raw);
        _inspectorTextChanged = false;
        InspectorIsNull = _currentCell.IsNull;
        InspectorStatus = InspectorIsJson ? "JSON detected · pretty printed" : "Reverted to stored value";
    }

    private async Task LoadPageAsync()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        IsLoading = true;
        LoadError = "";
        UpdateCommands();

        try
        {
            var request = new TableRequest
            {
                Table = _table.TableRef ?? new TableRef(_table.Schema, _table.Name),
                Columns = ToColumnInfos(),
                Offset = PageIndex * DefaultPageSize,
                Limit = DefaultPageSize,
                Filter = BuildFilterSpec(),
                SortColumn = SortColumn,
                SortDescending = SortDescending,
            };

            var page = await _session.Provider.ReadPageAsync(request, token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            ClearLiveRows();
            foreach (var values in page.Rows)
            {
                var row = BuildRow(values);
                Rows.Add(row);
            }

            SelectedRow = null;
            TotalRows = page.TotalCount;
            LastQueryMs = (int)page.ElapsedMs;
            var pageClamped = UpdatePageLabels();

            // The count query may report fewer pages than the offset we just
            // fetched. Re-read the clamped page instead of showing empty rows.
            if (pageClamped && (long)PageIndex * DefaultPageSize < TotalRows)
            {
                await LoadPageAsync();
                return;
            }

            SetStatus($"{Rows.Count:N0} rows loaded in {LastQueryMs} ms");
        }
        catch (OperationCanceledException)
        {
            // A newer request superseded this one.
        }
        catch (Exception ex)
        {
            LoadError = ex.GetBaseException().Message;
            SetStatus("Query failed: " + LoadError, true);
        }
        finally
        {
            IsLoading = false;
            RefreshCounters();
            UpdateCommands();
        }
    }

    private bool UpdatePageLabels()
    {
        PageCount = Math.Max(1, (int)Math.Ceiling(TotalRows / (double)DefaultPageSize));
        var clamped = false;
        if (PageIndex >= PageCount)
        {
            PageIndex = PageCount - 1;
            clamped = true;
        }

        PageLabel = $"Page {PageIndex + 1} of {PageCount}";
        if (TotalRows == 0)
        {
            RowRangeLabel = "No rows";
        }
        else
        {
            var start = (long)PageIndex * DefaultPageSize + 1;
            var end = Math.Min(TotalRows, (long)(PageIndex + 1) * DefaultPageSize);
            RowRangeLabel = $"{start:N0}–{end:N0} of {TotalRows:N0}";
        }

        OnPropertyChanged(nameof(CanNextPage));
        OnPropertyChanged(nameof(CanPreviousPage));
        UpdateCommands();
        return clamped;
    }

    private void RefreshCounters()
    {
        PendingCount = _trackedRows.Count(r => r.HasChanges);
        OnPropertyChanged(nameof(PendingLabel));
        OnPropertyChanged(nameof(HasPendingChanges));
        OnPropertyChanged(nameof(CanNextPage));
        OnPropertyChanged(nameof(CanPreviousPage));
        UpdateCommands();
    }

    private void SetStatus(string message, bool isError = false)
    {
        StatusMessage = message;
        IsStatusError = isError;
    }

    private FilterSpec? BuildFilterSpec()
    {
        if (!IsFilterActive)
        {
            return null;
        }

        return new FilterSpec
        {
            Column = FilterColumn,
            Operator = FilterOperator,
            Value = FilterValue.Trim(),
        };
    }

    private List<DbColumnInfo> ToColumnInfos() => Columns.Select(c => new DbColumnInfo
    {
        Name = c.Name,
        DataType = c.DataType,
        IsPrimaryKey = c.IsPrimaryKey,
        IsNullable = c.IsNullable,
        DefaultValue = string.IsNullOrWhiteSpace(c.DefaultValue) ? null : c.DefaultValue,
        Ordinal = c.Ordinal,
    }).ToList();

    private TableChangeSet BuildChangeSet(IReadOnlyList<RowViewModel> rows)
    {
        var set = new TableChangeSet
        {
            Table = _table.TableRef ?? new TableRef(_table.Schema, _table.Name),
            Columns = ToColumnInfos(),
        };

        foreach (var row in rows)
        {
            if (row.IsNew)
            {
                set.Changes.Add(new TableChange
                {
                    Kind = ChangeKind.Insert,
                    Values = row.Snapshot(),
                    OriginalValues = row.OriginalSnapshot(),
                });
            }
            else if (row.IsDeleted)
            {
                set.Changes.Add(new TableChange
                {
                    Kind = ChangeKind.Delete,
                    Values = row.Snapshot(),
                    OriginalValues = row.OriginalSnapshot(),
                });
            }
            else if (row.IsDirty)
            {
                set.Changes.Add(new TableChange
                {
                    Kind = ChangeKind.Update,
                    Values = row.ChangedSnapshot(),
                    OriginalValues = row.OriginalSnapshot(),
                });
            }
        }

        return set;
    }

    private RowViewModel BuildRow(object?[] values)
    {
        var dict = new Dictionary<string, CellData>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < Columns.Count; i++)
        {
            var raw = i < values.Length ? values[i] : null;
            var typed = raw is DBNull ? null : raw;
            dict[Columns[i].Name] = new CellData(ValueFormatter.Format(typed), typed);
        }

        var row = new RowViewModel(dict);
        Track(row);
        return row;
    }

    private void Track(RowViewModel row)
    {
        if (_trackedRows.Add(row))
        {
            row.Changed += OnRowChanged;
        }
    }

    private void Untrack(RowViewModel row)
    {
        if (_trackedRows.Remove(row))
        {
            row.Changed -= OnRowChanged;
        }
    }

    private void ClearLiveRows()
    {
        foreach (var row in Rows)
        {
            Untrack(row);
        }

        Rows.Clear();
    }

    private void OnRowChanged()
    {
        RefreshCounters();
    }

    private void UpdateCommands()
    {
        RaiseCanExecuteChanged(ApplyFilterCommand);
        RaiseCanExecuteChanged(ClearFilterCommand);
        RaiseCanExecuteChanged(AddRowCommand);
        RaiseCanExecuteChanged(DeleteRowCommand);
        RaiseCanExecuteChanged(SaveCommand);
        RaiseCanExecuteChanged(PreviewSqlCommand);
        RaiseCanExecuteChanged(DiscardChangesCommand);
        RaiseCanExecuteChanged(RefreshCommand);
        RaiseCanExecuteChanged(NextPageCommand);
        RaiseCanExecuteChanged(PrevPageCommand);
        OnPropertyChanged(nameof(CanNextPage));
        OnPropertyChanged(nameof(CanPreviousPage));
        OnPropertyChanged(nameof(CanSort));
    }

    private void RaiseInspectorCommands()
    {
        RaiseCanExecuteChanged(ApplyInspectorCommand);
        RaiseCanExecuteChanged(SetInspectorNullCommand);
        RaiseCanExecuteChanged(RevertInspectorCommand);
    }

    private static void RaiseCanExecuteChanged(ICommand command)
    {
        switch (command)
        {
            case RelayCommand relay:
                relay.RaiseCanExecuteChanged();
                break;
            case AsyncRelayCommand async:
                async.RaiseCanExecuteChanged();
                break;
        }
    }

    private static bool TryFormatJson(string text, out string pretty)
    {
        pretty = text;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        if (!(trimmed.StartsWith('{') || trimmed.StartsWith('[')))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            pretty = JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public override void OnClosed()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        foreach (var row in _trackedRows)
        {
            row.Changed -= OnRowChanged;
        }

        _trackedRows.Clear();
    }
}
