using System.Data.Common;
using Npgsql;
using NpgsqlTypes;
using SwiftBase.Core.Models;

namespace SwiftBase.Core.Providers;

public sealed class PostgresProvider : DatabaseProviderBase
{
    public PostgresProvider(ConnectionConfig config) : base(config)
    {
    }

    public override string QuoteIdentifier(string identifier) => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    protected override string QuoteTable(TableRef table)
    {
        var schema = string.IsNullOrWhiteSpace(table.Schema) ? "public" : table.Schema;
        return $"{QuoteIdentifier(schema)}.{QuoteIdentifier(table.Name)}";
    }

    protected override string TextCast(string quotedColumn) => $"CAST({quotedColumn} AS text)";

    protected override DbConnection CreateConnection()
    {
        // Direct connections authenticate the server with VerifyFull. A tunnel is
        // already an authenticated encrypted channel (verified SSH host key), and
        // its loopback endpoint cannot match the database certificate hostname.
        var viaTunnel = Config.UseSsh && Config.Engine != DatabaseEngine.Sqlite;
        var verifyCertificate = !Config.AllowUnverifiedTls && !viaTunnel;
        var rootCertificate = string.IsNullOrWhiteSpace(Config.RootCertificatePath)
            ? null
            : Config.RootCertificatePath;

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = string.IsNullOrWhiteSpace(Config.Host) ? "127.0.0.1" : Config.Host,
            Port = Config.EffectivePort > 0 ? Config.EffectivePort : 5432,
            Database = string.IsNullOrWhiteSpace(Config.Database) ? "postgres" : Config.Database,
            Username = Config.Username,
            Password = Config.Password ?? "",
            Timeout = 15,
            CommandTimeout = 120,
            SslMode = viaTunnel
                ? SslMode.Disable
                : verifyCertificate ? SslMode.VerifyFull : SslMode.Require,
            RootCertificate = verifyCertificate ? rootCertificate : null,
            CheckCertificateRevocation = verifyCertificate,
            IncludeErrorDetail = false,
            Pooling = true,
            MinPoolSize = 0,
            MaxPoolSize = 10,
            ApplicationName = "SwiftBase",
        };
        return new NpgsqlConnection(builder.ConnectionString);
    }

    protected override string SqlLiteral(object? value) => value is byte[] bytes
        ? "'\\x" + Convert.ToHexString(bytes) + "'::bytea"
        : base.SqlLiteral(value);

    protected override void ConfigureParameter(DbParameter parameter, object? value)
    {
        if (parameter is NpgsqlParameter npgsql)
        {
            if (value is string || value is null)
            {
                // Unknown lets PostgreSQL infer the column type, so text from
                // the grid can be written into numeric, uuid or timestamp columns.
                npgsql.NpgsqlDbType = NpgsqlDbType.Unknown;
            }

            npgsql.Value = value ?? DBNull.Value;
            return;
        }

        base.ConfigureParameter(parameter, value);
    }

    protected override async Task<IReadOnlyList<string>> LoadDatabasesAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        var databases = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT datname FROM pg_database WHERE datistemplate = false AND datallowconn = true ORDER BY datname;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            databases.Add(reader.GetString(0));
        }

        return databases;
    }

    protected override async Task<IReadOnlyList<DbTableInfo>> LoadSchemaAsync(DbConnection connection, string? database, CancellationToken cancellationToken)
    {
        var objects = new Dictionary<(string Schema, string Name), (DatabaseObjectKind Kind, List<DbColumnInfo> Columns)>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT table_schema, table_name, table_type FROM information_schema.tables " +
                "WHERE table_schema NOT IN ('pg_catalog','information_schema') " +
                "ORDER BY table_schema, table_name;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var type = reader.GetString(2);
                objects[(reader.GetString(0), reader.GetString(1))] = (
                    type == "VIEW" ? DatabaseObjectKind.View : DatabaseObjectKind.Table,
                    new List<DbColumnInfo>());
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT c.table_schema, c.table_name, c.column_name,
                       COALESCE(c.udt_name, c.data_type) AS data_type,
                       (c.is_nullable = 'YES') AS is_nullable,
                       c.column_default,
                       c.ordinal_position,
                       EXISTS (
                           SELECT 1
                           FROM information_schema.table_constraints tc
                           JOIN information_schema.key_column_usage kcu
                             ON tc.constraint_name = kcu.constraint_name
                            AND tc.table_schema = kcu.table_schema
                            AND tc.table_name = kcu.table_name
                           WHERE tc.constraint_type = 'PRIMARY KEY'
                             AND tc.table_schema = c.table_schema
                             AND tc.table_name = c.table_name
                             AND kcu.column_name = c.column_name
                       ) AS is_pk
                FROM information_schema.columns c
                WHERE c.table_schema NOT IN ('pg_catalog','information_schema')
                ORDER BY c.table_schema, c.table_name, c.ordinal_position;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var key = (reader.GetString(0), reader.GetString(1));
                if (!objects.TryGetValue(key, out var table))
                {
                    table = (DatabaseObjectKind.Table, new List<DbColumnInfo>());
                    objects[key] = table;
                }

                table.Columns.Add(new DbColumnInfo
                {
                    Name = reader.GetString(2),
                    DataType = reader.GetString(3),
                    IsNullable = reader.GetBoolean(4),
                    DefaultValue = reader.IsDBNull(5) ? null : reader.GetString(5),
                    Ordinal = reader.GetInt32(6),
                    IsPrimaryKey = reader.GetBoolean(7),
                });
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT routine_schema, routine_name FROM information_schema.routines " +
                "WHERE routine_schema NOT IN ('pg_catalog','information_schema') " +
                "GROUP BY routine_schema, routine_name ORDER BY routine_schema, routine_name;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var key = (reader.GetString(0), reader.GetString(1));
                objects.TryAdd(key, (DatabaseObjectKind.Function, new List<DbColumnInfo>()));
            }
        }

        return objects
            .OrderBy(pair => pair.Key.Schema, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pair => pair.Value.Kind)
            .ThenBy(pair => pair.Key.Name, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new DbTableInfo
            {
                Schema = pair.Key.Schema,
                Name = pair.Key.Name,
                Kind = pair.Value.Kind,
                Columns = pair.Value.Columns,
            })
            .ToList();
    }
}
