using System.Data.Common;
using System.Globalization;
using MySqlConnector;
using SwiftBase.Core.Models;

namespace SwiftBase.Core.Providers;

public sealed class MySqlProvider : DatabaseProviderBase
{
    public MySqlProvider(ConnectionConfig config) : base(config)
    {
    }

    public override string QuoteIdentifier(string identifier) => "`" + identifier.Replace("`", "``", StringComparison.Ordinal) + "`";

    protected override string QuoteTable(TableRef table)
    {
        var schema = string.IsNullOrWhiteSpace(table.Schema) ? Config.Database : table.Schema;
        return string.IsNullOrWhiteSpace(schema)
            ? QuoteIdentifier(table.Name)
            : $"{QuoteIdentifier(schema)}.{QuoteIdentifier(table.Name)}";
    }

    protected override string TextCast(string quotedColumn) => $"CAST({quotedColumn} AS CHAR)";

    protected override string LimitOne => " LIMIT 1";

    protected override string DefaultInsert(TableRef table) => $"INSERT INTO {QuoteTable(table)} () VALUES ()";

    protected override DbConnection CreateConnection()
    {
        // Direct connections verify both the CA and the hostname. The SSH tunnel
        // case is authenticated by its verified host key and terminates on loopback.
        var viaTunnel = Config.UseSsh && Config.Engine != DatabaseEngine.Sqlite;
        var verifyCertificate = !Config.AllowUnverifiedTls && !viaTunnel;

        var builder = new MySqlConnectionStringBuilder
        {
            Server = string.IsNullOrWhiteSpace(Config.Host) ? "127.0.0.1" : Config.Host,
            Port = (uint)(Config.EffectivePort > 0 ? Config.EffectivePort : 3306),
            Database = Config.Database,
            UserID = Config.Username,
            Password = Config.Password ?? "",
            SslMode = viaTunnel
                ? MySqlSslMode.Disabled
                : verifyCertificate ? MySqlSslMode.VerifyFull : MySqlSslMode.Required,
            SslCa = verifyCertificate && !string.IsNullOrWhiteSpace(Config.RootCertificatePath)
                ? Config.RootCertificatePath
                : null,
            SkipCertificateRevocationCheck = false,
            ConnectionTimeout = 15,
            DefaultCommandTimeout = 120,
            AllowUserVariables = true,
            ApplicationName = "SwiftBase",
            Pooling = true,
        };
        return new MySqlConnection(builder.ConnectionString);
    }

    protected override string SqlLiteral(object? value)
    {
        if (value is null or DBNull)
        {
            return "NULL";
        }

        if (value is bool boolean)
        {
            return boolean ? "TRUE" : "FALSE";
        }

        if (value is byte[] bytes)
        {
            return "X'" + Convert.ToHexString(bytes) + "'";
        }

        if (value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)
        {
            return Invariant(value);
        }

        // Round-trip temporal values instead of using the display format.
        var text = value switch
        {
            DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
            _ => Invariant(value),
        };

        // MySQL treats backslashes as escapes in string literals, so they must
        // be doubled before the quote is escaped.
        return "'" + text
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    protected override async Task<IReadOnlyList<string>> LoadDatabasesAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        var databases = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SHOW DATABASES;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            databases.Add(reader.GetString(0));
        }

        return databases;
    }

    protected override async Task<IReadOnlyList<DbTableInfo>> LoadSchemaAsync(DbConnection connection, string? database, CancellationToken cancellationToken)
    {
        var activeDatabase = string.IsNullOrWhiteSpace(database) ? Config.Database : database;
        var objects = new Dictionary<(string Schema, string Name), (DatabaseObjectKind Kind, List<DbColumnInfo> Columns)>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT TABLE_SCHEMA, TABLE_NAME, TABLE_TYPE FROM information_schema.TABLES " +
                "WHERE TABLE_SCHEMA = @db ORDER BY TABLE_NAME;";
            AddParameter(command, "@db", activeDatabase);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var type = reader.GetString(2);
                objects[(reader.GetString(0), reader.GetString(1))] = (
                    type.Contains("VIEW", StringComparison.OrdinalIgnoreCase) ? DatabaseObjectKind.View : DatabaseObjectKind.Table,
                    new List<DbColumnInfo>());
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME, COLUMN_TYPE, IS_NULLABLE,
                       COLUMN_DEFAULT, ORDINAL_POSITION, COLUMN_KEY
                FROM information_schema.COLUMNS
                WHERE TABLE_SCHEMA = @db
                ORDER BY TABLE_NAME, ORDINAL_POSITION;
                """;
            AddParameter(command, "@db", activeDatabase);
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
                    IsNullable = string.Equals(reader.GetString(4), "YES", StringComparison.OrdinalIgnoreCase),
                    DefaultValue = reader.IsDBNull(5) ? null : Convert.ToString(reader.GetValue(5)),
                    Ordinal = Convert.ToInt32(reader.GetValue(6)),
                    IsPrimaryKey = string.Equals(reader.GetString(7), "PRI", StringComparison.OrdinalIgnoreCase),
                });
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT ROUTINE_SCHEMA, ROUTINE_NAME FROM information_schema.ROUTINES WHERE ROUTINE_SCHEMA = @db ORDER BY ROUTINE_NAME;";
            AddParameter(command, "@db", activeDatabase);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var key = (reader.GetString(0), reader.GetString(1));
                objects.TryAdd(key, (DatabaseObjectKind.Function, new List<DbColumnInfo>()));
            }
        }

        return objects
            .OrderBy(pair => pair.Value.Kind)
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
