using SwiftBase.Core.Models;

namespace SwiftBase.Core.Interfaces;

/// <summary>One contract for every engine. All calls are asynchronous and cancellable.</summary>
public interface IDatabaseProvider : IAsyncDisposable
{
    DatabaseEngine Engine { get; }
    string EngineName { get; }
    ConnectionConfig Config { get; }

    Task<ConnectionTestResult> TestAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetDatabasesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DbTableInfo>> GetSchemaAsync(string? database = null, CancellationToken cancellationToken = default);

    Task<TablePage> ReadPageAsync(TableRequest request, CancellationToken cancellationToken = default);

    Task<QueryResult> ExecuteAsync(string sql, int maxRows = 5000, CancellationToken cancellationToken = default);

    Task<ChangeResult> ApplyChangesAsync(TableChangeSet changeSet, CancellationToken cancellationToken = default);

    /// <summary>Builds the exact SQL that <see cref="ApplyChangesAsync"/> would run, for the preview dialog.</summary>
    IReadOnlyList<string> BuildChangeScripts(TableChangeSet changeSet);

    /// <summary>Engine-correct quoted identifier for export scripts.</summary>
    string QuoteIdentifier(string identifier);

    /// <summary>Engine-correct, escaped SQL literal for export scripts (never built from display strings).</summary>
    string FormatSqlLiteral(object? value);
}
