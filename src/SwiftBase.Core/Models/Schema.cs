namespace SwiftBase.Core.Models;

public enum DatabaseObjectKind
{
    Table,
    View,
    Function,
}

public sealed record TableRef(string Schema, string Name)
{
    public override string ToString() => string.IsNullOrEmpty(Schema) ? Name : $"{Schema}.{Name}";
}

public sealed class DbColumnInfo
{
    public required string Name { get; init; }
    public string DataType { get; init; } = "text";
    public bool IsNullable { get; init; } = true;
    public string? DefaultValue { get; init; }
    public bool IsPrimaryKey { get; init; }
    public int Ordinal { get; init; }
}

public sealed class DbTableInfo
{
    public required string Name { get; init; }
    public string Schema { get; init; } = "";
    public DatabaseObjectKind Kind { get; init; } = DatabaseObjectKind.Table;
    public IReadOnlyList<DbColumnInfo> Columns { get; init; } = Array.Empty<DbColumnInfo>();
}

public sealed class FilterSpec
{
    public string Column { get; init; } = "";
    public string Operator { get; init; } = "contains";
    public string Value { get; init; } = "";
}
