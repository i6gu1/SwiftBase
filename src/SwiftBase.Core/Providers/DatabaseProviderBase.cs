using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SwiftBase.Core.Interfaces;
using SwiftBase.Core.Models;

namespace SwiftBase.Core.Providers;

/// <summary>
/// Shared paging, filtering, query and staging code for every engine.
/// All identifiers are quoted and every user value travels as a DbParameter.
/// </summary>
public abstract class DatabaseProviderBase : IDatabaseProvider
{
    protected DatabaseProviderBase(ConnectionConfig config)
    {
        Config = config;
    }

    public DatabaseEngine Engine => Config.Engine;
    public string EngineName => Config.EngineName;
    public ConnectionConfig Config { get; }

    protected abstract DbConnection CreateConnection();
    public abstract string QuoteIdentifier(string identifier);
    protected abstract string QuoteTable(TableRef table);
    protected abstract string TextCast(string quotedColumn);
    protected virtual string LimitOne => "";
    protected abstract Task<IReadOnlyList<string>> LoadDatabasesAsync(DbConnection connection, CancellationToken cancellationToken);
    protected abstract Task<IReadOnlyList<DbTableInfo>> LoadSchemaAsync(DbConnection connection, string? database, CancellationToken cancellationToken);
    protected virtual string DefaultInsert(TableRef table) => $"INSERT INTO {QuoteTable(table)} DEFAULT VALUES";
    protected virtual void ConfigureParameter(DbParameter parameter, object? value) => parameter.Value = value ?? DBNull.Value;
    protected virtual Task OnConnectionOpenedAsync(DbConnection connection, CancellationToken cancellationToken) => Task.CompletedTask;

    public virtual async ValueTask DisposeAsync()
    {
        await Task.CompletedTask;
        GC.SuppressFinalize(this);
    }

    public async Task<ConnectionTestResult> TestAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await OnConnectionOpenedAsync(connection, cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            command.CommandTimeout = 15;
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();
            return new ConnectionTestResult
            {
                Success = true,
                ElapsedMs = stopwatch.ElapsedMilliseconds,
                Message = $"Connected in {stopwatch.ElapsedMilliseconds} ms",
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new ConnectionTestResult
            {
                Success = false,
                ElapsedMs = stopwatch.ElapsedMilliseconds,
                Message = ex.GetBaseException().Message,
            };
        }
    }

    public async Task<IReadOnlyList<string>> GetDatabasesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await OnConnectionOpenedAsync(connection, cancellationToken).ConfigureAwait(false);
        return await LoadDatabasesAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DbTableInfo>> GetSchemaAsync(string? database = null, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await OnConnectionOpenedAsync(connection, cancellationToken).ConfigureAwait(false);
        return await LoadSchemaAsync(connection, database, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TablePage> ReadPageAsync(TableRequest request, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var limit = Math.Clamp(request.Limit, 1, 5000);
        var offset = Math.Max(0, request.Offset);

        var columns = request.Columns.Where(c => !string.IsNullOrWhiteSpace(c.Name)).ToList();
        if (columns.Count == 0)
        {
            return new TablePage { ElapsedMs = 0, TotalCount = 0 };
        }

        var selectList = string.Join(", ", columns.Select(c => QuoteIdentifier(c.Name)));
        var (whereSql, filterParameters) = BuildFilter(request.Filter, columns, null);
        var orderColumn = columns.FirstOrDefault(c =>
            string.Equals(c.Name, request.SortColumn, StringComparison.OrdinalIgnoreCase));
        orderColumn ??= columns.FirstOrDefault(c => c.IsPrimaryKey) ?? columns[0];
        var orderSql = QuoteIdentifier(orderColumn.Name) + (request.SortDescending ? " DESC" : " ASC");

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await OnConnectionOpenedAsync(connection, cancellationToken).ConfigureAwait(false);

        var total = 0L;
        await using (var countCommand = connection.CreateCommand())
        {
            countCommand.CommandTimeout = 120;
            countCommand.CommandText = $"SELECT COUNT(*) FROM {QuoteTable(request.Table)}{whereSql}";
            AddParameters(countCommand, filterParameters);
            var scalar = await countCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            total = scalar is null or DBNull ? 0 : Convert.ToInt64(scalar, CultureInfo.InvariantCulture);
        }

        var page = new TablePage { TotalCount = total };
        await using (var command = connection.CreateCommand())
        {
            command.CommandTimeout = 120;
            command.CommandText =
                $"SELECT {selectList} FROM {QuoteTable(request.Table)}{whereSql} ORDER BY {orderSql} LIMIT @__limit OFFSET @__offset";
            AddParameters(command, filterParameters);
            AddParameter(command, "@__limit", limit);
            AddParameter(command, "@__offset", offset);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var row = new object?[reader.FieldCount];
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }

                page.Rows.Add(row);
            }
        }

        stopwatch.Stop();
        return new TablePage
        {
            Rows = page.Rows,
            TotalCount = total,
            ElapsedMs = stopwatch.ElapsedMilliseconds,
        };
    }

    public async Task<QueryResult> ExecuteAsync(string sql, int maxRows = 5000, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        maxRows = Math.Clamp(maxRows, 1, 200_000);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await OnConnectionOpenedAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandTimeout = 300;
        command.CommandText = sql;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new QueryResult();
        var affected = -1;

        do
        {
            if (reader.FieldCount > 0)
            {
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    result.Columns.Add(new QueryColumn
                    {
                        Name = reader.GetName(i),
                        DataType = SafeDataType(reader, i),
                    });
                }

                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (result.Rows.Count >= maxRows)
                    {
                        result.Truncated = true;
                        break;
                    }

                    var row = new object?[reader.FieldCount];
                    for (var i = 0; i < reader.FieldCount; i++)
                    {
                        row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    }

                    result.Rows.Add(row);
                }

                affected = reader.RecordsAffected;
                break;
            }

            affected = reader.RecordsAffected;
        } while (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false));

        stopwatch.Stop();
        return new QueryResult
        {
            Columns = result.Columns,
            Rows = result.Rows,
            ElapsedMs = stopwatch.ElapsedMilliseconds,
            Truncated = result.Truncated,
            AffectedRows = Math.Max(0, affected),
            Message = result.Truncated
                ? $"Showing the first {maxRows:N0} rows"
                : result.Columns.Count == 0
                    ? $"Statement completed · {Math.Max(0, affected):N0} rows affected"
                    : $"{result.Rows.Count:N0} rows returned",
        };
    }

    public async Task<ChangeResult> ApplyChangesAsync(TableChangeSet changeSet, CancellationToken cancellationToken = default)
    {
        if (changeSet.Changes.Count == 0)
        {
            return new ChangeResult { Success = true, Message = "No pending changes." };
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await OnConnectionOpenedAsync(connection, cancellationToken).ConfigureAwait(false);

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var affectedTotal = 0;

            foreach (var change in changeSet.Changes)
            {
                if (change.Kind != ChangeKind.Insert)
                {
                    var affected = await CountMatchesAsync(connection, transaction, changeSet, change, cancellationToken).ConfigureAwait(false);
                    if (affected != 1)
                    {
                        await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                        return new ChangeResult
                        {
                            Success = false,
                            Message = affected == 0
                                ? "A row changed since it was loaded. Nothing was saved."
                                : "A change matched more than one row (table has no primary key). Nothing was saved.",
                        };
                    }
                }

                var (sql, parameters) = BuildStatement(changeSet, change);
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandTimeout = 120;
                command.CommandText = sql;
                AddParameters(command, parameters);
                var rows = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                affectedTotal += Math.Max(rows, 0);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            return new ChangeResult
            {
                Success = true,
                AffectedRows = affectedTotal,
                Message = $"Saved {changeSet.Changes.Count} change{(changeSet.Changes.Count == 1 ? "" : "s")} in {stopwatch.ElapsedMilliseconds} ms",
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new ChangeResult
            {
                Success = false,
                Message = ex.GetBaseException().Message,
            };
        }
    }

    protected virtual string TransactionBegin => "BEGIN;";
    protected virtual string TransactionCommit => "COMMIT;";

    public IReadOnlyList<string> BuildChangeScripts(TableChangeSet changeSet)
    {
        var scripts = new List<string>
        {
            $"-- SwiftBase · {changeSet.Changes.Count} staged change{(changeSet.Changes.Count == 1 ? "" : "s")} · transaction",
            TransactionBegin,
            "",
        };

        foreach (var change in changeSet.Changes)
        {
            var (sql, parameters) = BuildStatement(changeSet, change);
            var values = parameters.ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);

            // One pass only: values are never rescanned for parameter tokens.
            sql = Regex.Replace(
                sql,
                @"@[A-Za-z][A-Za-z0-9_]*",
                match => values.TryGetValue(match.Value, out var value) ? SqlLiteral(value) : match.Value);

            scripts.Add(sql + ";");
            scripts.Add("");
        }

        scripts.Add(TransactionCommit);
        return scripts;
    }

    protected virtual int MaxFilterValueLength => 4000;

    private (string Sql, List<(string Name, object? Value)> Parameters) BuildFilter(
        FilterSpec? filter,
        IReadOnlyList<DbColumnInfo> columns,
        HashSet<string>? parameterNames)
    {
        if (filter is null || string.IsNullOrWhiteSpace(filter.Column))
        {
            return ("", new List<(string, object?)>());
        }

        var column = columns.FirstOrDefault(c =>
            string.Equals(c.Name, filter.Column, StringComparison.OrdinalIgnoreCase));
        if (column is null)
        {
            return ("", new List<(string, object?)>());
        }

        var quoted = QuoteIdentifier(column.Name);
        var text = TextCast(quoted);
        var parameters = new List<(string, object?)>();
        var name = NextParameterName(parameterNames, "f");
        var op = (filter.Operator ?? "contains").Trim().ToLowerInvariant();
        var value = filter.Value ?? "";
        if (value.Length > MaxFilterValueLength)
        {
            value = value[..MaxFilterValueLength];
        }

        switch (op)
        {
            case "is empty":
                return ($" WHERE ({quoted} IS NULL OR {text} = '')", parameters);

            case "is not empty":
                return ($" WHERE ({quoted} IS NOT NULL AND {text} <> '')", parameters);

            case "equals":
                parameters.Add((name, value));
                return ($" WHERE LOWER({text}) = LOWER({name})", parameters);

            case "starts with":
                parameters.Add((name, EscapeLike(value) + "%"));
                return ($" WHERE LOWER({text}) LIKE LOWER({name}) ESCAPE '!'", parameters);

            default:
                parameters.Add((name, "%" + EscapeLike(value) + "%"));
                return ($" WHERE LOWER({text}) LIKE LOWER({name}) ESCAPE '!'", parameters);
        }
    }

    private static string NextParameterName(HashSet<string>? used, string prefix)
    {
        var index = used?.Count ?? 0;
        var name = "@" + prefix + index.ToString(CultureInfo.InvariantCulture);
        while (used?.Contains(name) == true)
        {
            index++;
            name = "@" + prefix + index.ToString(CultureInfo.InvariantCulture);
        }

        used?.Add(name);
        return name;
    }

    private static string EscapeLike(string value) =>
        value.Replace("!", "!!", StringComparison.Ordinal)
             .Replace("%", "!%", StringComparison.Ordinal)
             .Replace("_", "!_", StringComparison.Ordinal);

    private static IReadOnlyList<DbColumnInfo> ConcurrencyColumns(TableChangeSet changeSet, TableChange change)
    {
        if (!changeSet.Columns.Any(c => c.IsPrimaryKey))
        {
            return changeSet.Columns.ToList();
        }

        return changeSet.Columns
            .Where(c => c.IsPrimaryKey || change.OriginalValues.ContainsKey(c.Name))
            .ToList();
    }

    private (string Sql, List<(string Name, object? Value)> Parameters) BuildStatement(
        TableChangeSet changeSet,
        TableChange change)
    {
        var table = QuoteTable(changeSet.Table);
        var parameters = new List<(string Name, object? Value)>();
        var parameterIndex = 0;

        if (change.Kind == ChangeKind.Insert)
        {
            var insertColumns = new List<string>();
            var values = new List<string>();
            var primaryKeys = changeSet.Columns.Where(c => c.IsPrimaryKey).ToHashSet();

            foreach (var column in changeSet.Columns)
            {
                if (!change.Values.TryGetValue(column.Name, out var value))
                {
                    continue;
                }

                // Let the database generate auto-increment / default primary keys.
                // Typed values are now preserved, so do not coerce them to strings here.
                if (primaryKeys.Contains(column) && (value is null or DBNull || (value is string text && text.Length == 0)))
                {
                    continue;
                }

                var name = "@i" + parameterIndex++.ToString(CultureInfo.InvariantCulture);
                insertColumns.Add(QuoteIdentifier(column.Name));
                values.Add(name);
                parameters.Add((name, value));
            }

            if (insertColumns.Count == 0)
            {
                return (DefaultInsert(changeSet.Table), parameters);
            }

            return ($"INSERT INTO {table} ({string.Join(", ", insertColumns)}) VALUES ({string.Join(", ", values)})", parameters);
        }

        var keys = ConcurrencyColumns(changeSet, change);

        var whereParts = new List<string>();
        foreach (var column in keys)
        {
            change.OriginalValues.TryGetValue(column.Name, out var original);
            var quoted = QuoteIdentifier(column.Name);
            if (original is null)
            {
                whereParts.Add($"{quoted} IS NULL");
            }
            else
            {
                var name = "@w" + parameterIndex++.ToString(CultureInfo.InvariantCulture);
                whereParts.Add($"{quoted} = {name}");
                parameters.Add((name, original));
            }
        }

        var where = string.Join(" AND ", whereParts);
        if (change.Kind == ChangeKind.Delete)
        {
            return ($"DELETE FROM {table} WHERE {where}{LimitOne}", parameters);
        }

        var setParts = new List<string>();
        foreach (var column in changeSet.Columns)
        {
            if (!change.Values.TryGetValue(column.Name, out var value))
            {
                continue;
            }

            var name = "@s" + parameterIndex++.ToString(CultureInfo.InvariantCulture);
            setParts.Add($"{QuoteIdentifier(column.Name)} = {name}");
            parameters.Add((name, value));
        }

        if (setParts.Count == 0)
        {
            setParts.Add($"{QuoteIdentifier(keys[0].Name)} = {QuoteIdentifier(keys[0].Name)}");
        }

        return ($"UPDATE {table} SET {string.Join(", ", setParts)} WHERE {where}{LimitOne}", parameters);
    }

    private async Task<int> CountMatchesAsync(
        DbConnection connection,
        DbTransaction transaction,
        TableChangeSet changeSet,
        TableChange change,
        CancellationToken cancellationToken)
    {
        var keys = ConcurrencyColumns(changeSet, change);

        var whereParts = new List<string>();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = 60;

        var index = 0;
        foreach (var column in keys)
        {
            change.OriginalValues.TryGetValue(column.Name, out var original);
            var quoted = QuoteIdentifier(column.Name);
            if (original is null)
            {
                whereParts.Add($"{quoted} IS NULL");
            }
            else
            {
                var name = "@c" + index++.ToString(CultureInfo.InvariantCulture);
                whereParts.Add($"{quoted} = {name}");
                AddParameter(command, name, original);
            }
        }

        command.CommandText = $"SELECT COUNT(*) FROM {QuoteTable(changeSet.Table)} WHERE {string.Join(" AND ", whereParts)}";
        var scalar = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return scalar is null or DBNull ? 0 : Convert.ToInt32(scalar, CultureInfo.InvariantCulture);
    }

    private void AddParameters(DbCommand command, IEnumerable<(string Name, object? Value)> parameters)
    {
        foreach (var (name, value) in parameters)
        {
            AddParameter(command, name, value);
        }
    }

    protected DbParameter AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        ConfigureParameter(parameter, value);
        command.Parameters.Add(parameter);
        return parameter;
    }

    private static string SafeDataType(DbDataReader reader, int index)
    {
        try
        {
            return reader.GetDataTypeName(index);
        }
        catch
        {
            return "";
        }
    }

    /// <summary>Public entry point used by export generation. Always engine-correct.</summary>
    public string FormatSqlLiteral(object? value) => SqlLiteral(value);

    protected virtual string SqlLiteral(object? value)
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

        if (value is DateTime dateTime)
        {
            return Quote(dateTime.ToString("O", CultureInfo.InvariantCulture));
        }

        if (value is DateTimeOffset dateTimeOffset)
        {
            return Quote(dateTimeOffset.ToString("O", CultureInfo.InvariantCulture));
        }

        if (value is Guid guid)
        {
            return Quote(guid.ToString("D"));
        }

        if (value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)
        {
            return Invariant(value);
        }

        return Quote(Invariant(value));
    }

    /// <summary>ANSI/SQLite/PostgreSQL string literal. MySQL overrides with backslash handling.</summary>
    protected static string Quote(string text) =>
        "'" + text.Replace("'", "''", StringComparison.Ordinal) + "'";

    protected static string Invariant(object? value) =>
        value is null or DBNull ? "" : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
}
