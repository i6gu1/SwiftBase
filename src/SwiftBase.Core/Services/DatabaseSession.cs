using SwiftBase.Core.Interfaces;
using SwiftBase.Core.Models;
using SwiftBase.Core.Providers;
using SwiftBase.Core.Security;

namespace SwiftBase.Core.Services;

/// <summary>
/// Owns a provider and, when configured, its SSH tunnel. Switching database
/// opens a fresh session against the same server, then disposes the old one.
/// </summary>
public sealed class DatabaseSession : IAsyncDisposable
{
    private SshTunnelManager? _tunnel;

    private DatabaseSession(ConnectionConfig config, string activeDatabase, IDatabaseProvider provider, SshTunnelManager? tunnel)
    {
        Config = config;
        ActiveDatabase = activeDatabase;
        Provider = provider;
        _tunnel = tunnel;
    }

    public ConnectionConfig Config { get; }
    public string ActiveDatabase { get; }
    public IDatabaseProvider Provider { get; }

    public static async Task<DatabaseSession> OpenAsync(
        ConnectionConfig source,
        string? database,
        CancellationToken cancellationToken = default)
    {
        var config = source.CloneForDatabase(database);
        if (config.Engine == DatabaseEngine.Sqlite)
        {
            config.Database = source.Database;
        }

        SshTunnelManager? tunnel = null;
        if (config.UseSsh && config.Engine != DatabaseEngine.Sqlite)
        {
            tunnel = await SshTunnelManager.StartAsync(
                config.Ssh,
                config.Host,
                config.EffectivePort,
                cancellationToken).ConfigureAwait(false);

            config.Host = "127.0.0.1";
            config.Port = tunnel.LocalPort;
        }

        try
        {
            var provider = ProviderFactory.Create(config);
            return new DatabaseSession(config, config.Database, provider, tunnel);
        }
        catch
        {
            tunnel?.Dispose();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Provider.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // ignored
        }

        _tunnel?.Dispose();
        _tunnel = null;
    }
}
