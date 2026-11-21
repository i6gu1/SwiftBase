using SwiftBase.Core.Interfaces;
using SwiftBase.Core.Models;

namespace SwiftBase.Core.Providers;

public static class ProviderFactory
{
    public static IDatabaseProvider Create(ConnectionConfig config) => config.Engine switch
    {
        DatabaseEngine.Postgres => new PostgresProvider(config),
        DatabaseEngine.MySql => new MySqlProvider(config),
        _ => new SqliteProvider(config),
    };
}
