using System.Data.Common;
using System.Globalization;
using Microsoft.Data.Sqlite;
using SwiftBase.Core.Models;

namespace SwiftBase.Core.Providers;

public sealed class SqliteProvider : DatabaseProviderBase
{
    public SqliteProvider(ConnectionConfig config) : base(config)
    {
    }

    public override string QuoteIdentifier(string identifier) => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    protected override string QuoteTable(TableRef table)
    {
        var schema = string.IsNullOrWhiteSpace(table.Schema) ? "main" : table.Schema;
        return $"{QuoteIdentifier(schema)}.{QuoteIdentifier(table.Name)}";
    }

    protected override string TextCast(string quotedColumn) => $"CAST({quotedColumn} AS TEXT)";

    protected override DbConnection CreateConnection()
    {
        var path = Config.Database;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = true,
            DefaultTimeout = 15,
        };
        return new SqliteConnection(builder.ConnectionString);
    }

    protected override async Task OnConnectionOpenedAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override Task<IReadOnlyList<string>> LoadDatabasesAsync(DbConnection connection, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>(new[] { "main" });

    protected override async Task<IReadOnlyList<DbTableInfo>> LoadSchemaAsync(DbConnection connection, string? database, CancellationToken cancellationToken)
    {
        var objects = new List<(string Name, DatabaseObjectKind Kind)>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT name, type FROM sqlite_master " +
                "WHERE type IN ('table','view') AND name NOT LIKE 'sqlite_%' " +
                "ORDER BY CASE type WHEN 'table' THEN 0 ELSE 1 END, name;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var type = reader.GetString(1);
                objects.Add((reader.GetString(0), type == "view" ? DatabaseObjectKind.View : DatabaseObjectKind.Table));
            }
        }

        var tables = new List<DbTableInfo>(objects.Count);
        foreach (var item in objects)
        {
            var columns = new List<DbColumnInfo>();
            await using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info({QuoteIdentifier(item.Name)});";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var type = reader.IsDBNull(2) ? "text" : reader.GetString(2);
                columns.Add(new DbColumnInfo
                {
                    Name = reader.GetString(1),
                    DataType = string.IsNullOrWhiteSpace(type) ? "text" : type,
                    IsNullable = reader.GetInt32(3) == 0 && reader.GetInt32(5) == 0,
                    DefaultValue = reader.IsDBNull(4) ? null : reader.GetString(4),
                    IsPrimaryKey = reader.GetInt32(5) > 0,
                    Ordinal = reader.GetInt32(0) + 1,
                });
            }

            tables.Add(new DbTableInfo
            {
                Name = item.Name,
                Schema = "main",
                Kind = item.Kind,
                Columns = columns,
            });
        }

        return tables;
    }
}
