using System.Collections.ObjectModel;
using System.Data;
using System.Text;
using System.Text.Json;
using System.Windows.Input;
using SwiftBase.Core.Models;
using SwiftBase.Core.Services;
using SwiftBase.Models;

namespace SwiftBase.ViewModels;

public sealed record SqlCompletion(string Text, string Description);

/// <summary>SQL console tab: editor, cancellation, results grid and exports.</summary>
public sealed class QueryTabViewModel : TabViewModel
{
    private readonly DatabaseSession _session;
    private readonly IReadOnlyList<DbTable> _tables;
    private readonly string _database;
    private readonly int _number;

    private string _editorText;
    private string _selectionText = "";
    private DataView? _resultView;
    private QueryResult? _lastResult;
    private string _resultMessage = "";
    private string _errorMessage = "";
    private bool _hasError;
    private bool _isRunning;
    private bool _hasResults;
    private long _elapsedMs;
    private int _returnedRows;
    private bool _isTruncated;
    private CancellationTokenSource? _cts;

    public QueryTabViewModel(
        DatabaseSession session,
        string database,
        IReadOnlyList<DbTable> tables,
        int number,
        Action<TabViewModel> close)
        : base(close, $"Query {number}", "SQL", $"query · {database}")
    {
        _session = session;
        _database = database;
        _tables = tables;
        _number = number;

        var firstTable = tables.FirstOrDefault(t => t.Kind == TableKind.Table);
        _editorText = firstTable is null
            ? "select 1 as answer;"
            : $"select *\nfrom {firstTable.Name}\nlimit 500;";

        RunCommand = new AsyncRelayCommand(() => RunFromEditor(SelectionText));
        CancelCommand = new RelayCommand(Cancel, () => IsRunning);
        RunSelectionCommand = new AsyncRelayCommand(() => RunFromEditor(SelectionText), () => !IsRunning);
        Completions = BuildCompletions();
    }

    public string Database => _database;
    public int Number => _number;
    public override bool IsQueryTab => true;

    public string EditorText
    {
        get => _editorText;
        set => Set(ref _editorText, value);
    }

    public string SelectionText
    {
        get => _selectionText;
        set => Set(ref _selectionText, value);
    }

    public DataView? ResultView
    {
        get => _resultView;
        private set => Set(ref _resultView, value);
    }

    public string ResultMessage
    {
        get => _resultMessage;
        private set => Set(ref _resultMessage, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => Set(ref _errorMessage, value);
    }

    public bool HasError
    {
        get => _hasError;
        private set => Set(ref _hasError, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (Set(ref _isRunning, value))
            {
                (CancelCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (RunSelectionCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                RunCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasResults
    {
        get => _hasResults;
        private set => Set(ref _hasResults, value);
    }

    public long ElapsedMs
    {
        get => _elapsedMs;
        private set
        {
            if (Set(ref _elapsedMs, value))
            {
                OnPropertyChanged(nameof(ElapsedLabel));
            }
        }
    }

    public int ReturnedRows
    {
        get => _returnedRows;
        private set
        {
            if (Set(ref _returnedRows, value))
            {
                OnPropertyChanged(nameof(ResultTitle));
            }
        }
    }

    public bool IsTruncated
    {
        get => _isTruncated;
        private set => Set(ref _isTruncated, value);
    }

    public string ElapsedLabel => ElapsedMs == 0 ? "" : $"{ElapsedMs:N0} ms";
    public string ResultTitle => ReturnedRows == 0 ? "RESULT" : $"RESULT · {ReturnedRows:N0} ROWS";
    public bool CanExport => _lastResult is { HasRows: true };
    public IReadOnlyList<SqlCompletion> Completions { get; }

    public AsyncRelayCommand RunCommand { get; }
    public ICommand RunSelectionCommand { get; }
    public ICommand CancelCommand { get; }

    public async Task RunFromEditor(string? selection)
    {
        if (IsRunning)
        {
            return;
        }

        var sql = string.IsNullOrWhiteSpace(selection) ? EditorText : selection;
        if (string.IsNullOrWhiteSpace(sql))
        {
            ErrorMessage = "Write a SQL statement first.";
            HasError = true;
            return;
        }

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        IsRunning = true;
        HasError = false;
        ErrorMessage = "";
        ResultMessage = "Running…";
        ClearResults();

        try
        {
            var result = await _session.Provider.ExecuteAsync(sql, 5000, token);

            if (token.IsCancellationRequested)
            {
                ClearResults();
                ResultMessage = "Query cancelled";
                return;
            }

            _lastResult = result;
            ElapsedMs = result.ElapsedMs;
            ReturnedRows = result.Rows.Count;
            IsTruncated = result.Truncated;
            ResultMessage = result.Message;
            HasResults = result.Columns.Count > 0;
            ResultView = BuildView(result);
            OnPropertyChanged(nameof(CanExport));
        }
        catch (OperationCanceledException)
        {
            ClearResults();
            ResultMessage = "Query cancelled";
        }
        catch (Exception ex)
        {
            ClearResults();
            HasError = true;
            ErrorMessage = ex.GetBaseException().Message;
            ResultMessage = "Query failed";
        }
        finally
        {
            IsRunning = false;
        }
    }

    public string BuildCsv()
    {
        if (_lastResult is null)
        {
            return "";
        }

        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", _lastResult.Columns.Select(c => EscapeCsv(c.Name))));
        foreach (var row in _lastResult.Rows)
        {
            builder.AppendLine(string.Join(",", row.Select(value => EscapeCsv(ValueFormatter.FormatExport(value) ?? ""))));
        }

        return builder.ToString();
    }

    public string BuildJson()
    {
        if (_lastResult is null)
        {
            return "[]";
        }

        var rows = new List<Dictionary<string, object?>>();
        foreach (var row in _lastResult.Rows)
        {
            var item = new Dictionary<string, object?>();
            for (var i = 0; i < _lastResult.Columns.Count; i++)
            {
                var value = i < row.Length ? row[i] : null;
                item[_lastResult.Columns[i].Name] = value is DBNull ? null : value;
            }

            rows.Add(item);
        }

        return JsonSerializer.Serialize(rows, new JsonSerializerOptions
        {
            WriteIndented = true,
        });
    }

    public string BuildInserts()
    {
        if (_lastResult is null)
        {
            return "";
        }

        var provider = _session.Provider;
        var table = provider.QuoteIdentifier("exported_rows");
        var columns = string.Join(", ", _lastResult.Columns.Select(c => provider.QuoteIdentifier(c.Name)));
        var builder = new StringBuilder();
        foreach (var row in _lastResult.Rows)
        {
            // Typed values go through the engine provider, never through display text.
            var values = string.Join(", ", row.Select(provider.FormatSqlLiteral));
            builder.AppendLine($"INSERT INTO {table} ({columns}) VALUES ({values});");
        }

        return builder.ToString();
    }

    private DataView? BuildView(QueryResult result)
    {
        if (result.Columns.Count == 0)
        {
            return null;
        }

        var table = new DataTable();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in result.Columns)
        {
            var name = column.Name;
            var candidate = name;
            var suffix = 2;
            while (!names.Add(candidate))
            {
                candidate = $"{name}_{suffix++}";
            }

            var dataColumn = new DataColumn(candidate, typeof(object))
            {
                Caption = name,
            };
            table.Columns.Add(dataColumn);
        }

        foreach (var row in result.Rows)
        {
            var values = new object?[result.Columns.Count];
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = i < row.Length ? row[i] ?? DBNull.Value : DBNull.Value;
            }

            table.Rows.Add(values);
        }

        return table.DefaultView;
    }

    private void ClearResults()
    {
        _lastResult = null;
        ResultView = null;
        HasResults = false;
        IsTruncated = false;
        ReturnedRows = 0;
        ElapsedMs = 0;
        OnPropertyChanged(nameof(CanExport));
    }

    private void Cancel()
    {
        if (_cts is not null)
        {
            _cts.Cancel();
            ResultMessage = "Cancelling…";
        }
    }

    private IReadOnlyList<SqlCompletion> BuildCompletions()
    {
        var words = new List<SqlCompletion>();
        var keywords = new[]
        {
            "SELECT", "FROM", "WHERE", "INSERT INTO", "UPDATE", "DELETE FROM", "VALUES", "SET", "JOIN",
            "LEFT JOIN", "INNER JOIN", "ON", "GROUP BY", "ORDER BY", "HAVING", "LIMIT", "OFFSET", "AS",
            "AND", "OR", "NOT", "NULL", "IS NULL", "IS NOT NULL", "LIKE", "IN", "EXISTS", "COUNT", "SUM",
            "AVG", "MIN", "MAX", "DISTINCT", "CASE", "WHEN", "THEN", "ELSE", "END", "UNION", "CREATE TABLE",
            "ALTER TABLE", "DROP TABLE", "BEGIN", "COMMIT", "ROLLBACK",
        };

        words.AddRange(keywords.Select(k => new SqlCompletion(k, "SQL keyword")));
        foreach (var table in _tables)
        {
            words.Add(new SqlCompletion(table.Name, $"{table.KindLabel} · {table.Schema}"));
            foreach (var column in table.Columns)
            {
                words.Add(new SqlCompletion(column.Name, $"{column.DataType} · {table.Name}"));
            }
        }

        return words
            .GroupBy(w => w.Text, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static string EscapeCsv(string value)
    {
        // Spreadsheet apps execute cells beginning with these characters.
        // A leading apostrophe forces text without changing the exported value
        // when the file is read as plain text.
        var first = value.TrimStart().FirstOrDefault();
        var safe = first is '=' or '+' or '-' or '@' or '\t' or '\r'
            ? "'" + value
            : value;

        if (safe.Contains('"') || safe.Contains(',') || safe.Contains('\n') || safe.Contains('\r'))
        {
            return "\"" + safe.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        }

        return safe;
    }

    public override void OnClosed()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
