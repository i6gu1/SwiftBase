namespace SwiftBase.Core.Models;

public sealed class TableRequest
{
    public required TableRef Table { get; init; }
    public required IReadOnlyList<DbColumnInfo> Columns { get; init; }
    public int Offset { get; init; }
    public int Limit { get; init; } = 500;
    public FilterSpec? Filter { get; init; }
    public string? SortColumn { get; init; }
    public bool SortDescending { get; init; }
}

public sealed class TablePage
{
    public List<object?[]> Rows { get; init; } = new();
    public long TotalCount { get; init; }
    public long ElapsedMs { get; init; }
    public bool Truncated { get; init; }
}

public sealed class QueryColumn
{
    public required string Name { get; init; }
    public string DataType { get; init; } = "";
}

public sealed class QueryResult
{
    public List<QueryColumn> Columns { get; init; } = new();
    public List<object?[]> Rows { get; init; } = new();
    public long ElapsedMs { get; set; }
    public int AffectedRows { get; set; }
    public bool Truncated { get; set; }
    public string Message { get; set; } = "";

    public bool HasRows => Columns.Count > 0;
}

public enum ChangeKind
{
    Insert,
    Update,
    Delete,
}

/// <summary>
/// A single staged edit. Values keep their original CLR type so BLOBs, timestamps
/// and TIME values survive a save unchanged; the grid's text is display-only.
/// </summary>
public sealed class TableChange
{
    public ChangeKind Kind { get; init; }
    public Dictionary<string, object?> Values { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, object?> OriginalValues { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class TableChangeSet
{
    public required TableRef Table { get; init; }
    public required IReadOnlyList<DbColumnInfo> Columns { get; init; }
    public List<TableChange> Changes { get; init; } = new();
}

public sealed class ChangeResult
{
    public bool Success { get; init; }
    public int AffectedRows { get; init; }
    public string Message { get; init; } = "";
}

public sealed class ConnectionTestResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public long ElapsedMs { get; init; }
}
