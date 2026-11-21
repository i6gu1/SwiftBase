using System.Collections.ObjectModel;
using SwiftBase.Core.Models;

namespace SwiftBase.Models;

public enum DbEngine
{
    Sqlite,
    Postgres,
    MySql,
}

public enum TableKind
{
    Table,
    View,
    Function,
}

public sealed class ConnectionProfile
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "";
    public DbEngine Engine { get; init; } = DbEngine.Sqlite;
    public string Host { get; init; } = "";
    public string Database { get; init; } = "";
    public string Environment { get; init; } = "Local";
    public bool IsDemo { get; init; }
    public ConnectionConfig? Config { get; init; }

    public string Badge => Engine switch
    {
        DbEngine.Postgres => "PG",
        DbEngine.MySql => "MY",
        _ => "SQ",
    };

    public string BadgeColor => Engine switch
    {
        DbEngine.Postgres => "#173253",
        DbEngine.MySql => "#41260F",
        _ => "#0E3024",
    };

    public string BadgeForeground => Engine switch
    {
        DbEngine.Postgres => "#74B8FF",
        DbEngine.MySql => "#FFAE70",
        _ => "#54DFA2",
    };

    public string EnvironmentColor => Environment == "Production" ? "#F87171" : "#34D399";
    public bool IsProduction => Environment == "Production";

    public string Subtitle => string.IsNullOrWhiteSpace(Host)
        ? Database
        : $"{Host} · {Database}";

    public string EngineName => Engine switch
    {
        DbEngine.Postgres => "PostgreSQL",
        DbEngine.MySql => "MySQL",
        _ => "SQLite",
    };

    public string GroupName => Environment == "Production" ? "Production" : IsDemo ? "Demo" : "Saved connections";

    public static ConnectionProfile FromConfig(ConnectionConfig config)
    {
        var engine = config.Engine switch
        {
            DatabaseEngine.Postgres => DbEngine.Postgres,
            DatabaseEngine.MySql => DbEngine.MySql,
            _ => DbEngine.Sqlite,
        };

        var host = config.Engine == DatabaseEngine.Sqlite
            ? ""
            : config.Host + (config.EffectivePort > 0 ? ":" + config.EffectivePort : "");

        return new ConnectionProfile
        {
            Id = config.Id.ToString("N"),
            Name = config.Name,
            Engine = engine,
            Host = host,
            Database = config.Engine == DatabaseEngine.Sqlite
                ? System.IO.Path.GetFileName(config.Database)
                : config.Database,
            Environment = config.Environment,
            IsDemo = config.IsDemo,
            Config = config,
        };
    }

    public static ConnectionConfig ToConfig(ConnectionProfile profile)
    {
        if (profile.Config is not null)
        {
            return profile.Config;
        }

        var engine = profile.Engine switch
        {
            DbEngine.Postgres => DatabaseEngine.Postgres,
            DbEngine.MySql => DatabaseEngine.MySql,
            _ => DatabaseEngine.Sqlite,
        };

        return new ConnectionConfig
        {
            Id = Guid.TryParse(profile.Id, out var id) ? id : Guid.NewGuid(),
            Name = profile.Name,
            Engine = engine,
            Database = profile.Database,
            Environment = profile.Environment,
            IsDemo = profile.IsDemo,
        };
    }
}

public sealed class ConnectionGroup
{
    public string Name { get; init; } = "";
    public IReadOnlyList<ConnectionProfile> Items { get; init; } = Array.Empty<ConnectionProfile>();
}

public sealed class DbColumn
{
    public string Name { get; init; } = "";
    public string DataType { get; init; } = "text";
    public bool IsPrimaryKey { get; init; }
    public bool IsNullable { get; init; } = true;
    public string DefaultValue { get; init; } = "";
    public int Ordinal { get; init; }
    public string KeyLabel => IsPrimaryKey ? "PK" : "";
    public string NullableLabel => IsNullable ? "NULL" : "NOT NULL";
    public string DefaultLabel => string.IsNullOrWhiteSpace(DefaultValue) ? "—" : DefaultValue;
}

public sealed class DbTable
{
    public string Name { get; init; } = "";
    public TableKind Kind { get; init; } = TableKind.Table;
    public string Schema { get; init; } = "public";
    public string Database { get; init; } = "";
    public IReadOnlyList<DbColumn> Columns { get; init; } = Array.Empty<DbColumn>();
    public ObservableCollection<RowViewModel> Rows { get; } = new();
    public bool IsLive { get; init; }
    public TableRef? TableRef { get; init; }

    public string Glyph => Kind switch
    {
        TableKind.View => "◫",
        TableKind.Function => "ƒ",
        _ => "▦",
    };

    public string KindLabel => Kind switch
    {
        TableKind.View => "view",
        TableKind.Function => "function",
        _ => "table",
    };

    public string KindBadge => KindLabel.ToUpperInvariant();

    public string QualifiedName => string.IsNullOrWhiteSpace(Schema) ? Name : $"{Schema}.{Name}";

    public bool HasPrimaryKey => Columns.Any(c => c.IsPrimaryKey);

    public static DbTable FromInfo(DbTableInfo info)
    {
        var kind = info.Kind switch
        {
            DatabaseObjectKind.View => TableKind.View,
            DatabaseObjectKind.Function => TableKind.Function,
            _ => TableKind.Table,
        };

        return new DbTable
        {
            Name = info.Name,
            Schema = info.Schema,
            Kind = kind,
            IsLive = true,
            TableRef = new TableRef(info.Schema, info.Name),
            Columns = info.Columns.Select(c => new DbColumn
            {
                Name = c.Name,
                DataType = c.DataType,
                IsPrimaryKey = c.IsPrimaryKey,
                IsNullable = c.IsNullable,
                DefaultValue = c.DefaultValue ?? "",
                Ordinal = c.Ordinal,
            }).ToList(),
        };
    }
}

public enum PaletteKind
{
    Table,
    Action,
    Connection,
}

public sealed class PaletteItem
{
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public string Glyph { get; init; } = "▦";
    public PaletteKind Kind { get; init; } = PaletteKind.Table;
    public DbTable? Table { get; init; }
    public ConnectionProfile? Connection { get; init; }
    public string ActionKey { get; init; } = "";
    public string SearchText => $"{Title} {Subtitle}";
}
