namespace SwiftBase.Core.Models;

public enum DatabaseEngine
{
    Sqlite,
    Postgres,
    MySql,
}

public sealed class SshConfig
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 22;
    public string Username { get; set; } = "";
    public string? Password { get; set; }
    public string? PrivateKeyPath { get; set; }
    public string? Passphrase { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(Username);
}

/// <summary>Everything required to open a connection. The password is encrypted at rest.</summary>
public sealed class ConnectionConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public DatabaseEngine Engine { get; set; } = DatabaseEngine.Sqlite;
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; }
    public string Database { get; set; } = "";
    public string Username { get; set; } = "";
    public string? Password { get; set; }
    public string Environment { get; set; } = "Local";
    public bool UseSsh { get; set; }
    public SshConfig Ssh { get; set; } = new();
    public bool IsDemo { get; set; }

    public int EffectivePort => Port > 0
        ? Port
        : Engine switch
        {
            DatabaseEngine.Postgres => 5432,
            DatabaseEngine.MySql => 3306,
            _ => 0,
        };

    public string EngineName => Engine switch
    {
        DatabaseEngine.Postgres => "PostgreSQL",
        DatabaseEngine.MySql => "MySQL",
        _ => "SQLite",
    };

    public ConnectionConfig CloneForDatabase(string? database)
    {
        var clone = new ConnectionConfig
        {
            Id = Id,
            Name = Name,
            Engine = Engine,
            Host = Host,
            Port = Port,
            Database = database ?? Database,
            Username = Username,
            Password = Password,
            Environment = Environment,
            UseSsh = UseSsh,
            IsDemo = IsDemo,
            Ssh = new SshConfig
            {
                Host = Ssh.Host,
                Port = Ssh.Port,
                Username = Ssh.Username,
                Password = Ssh.Password,
                PrivateKeyPath = Ssh.PrivateKeyPath,
                Passphrase = Ssh.Passphrase,
            },
        };
        return clone;
    }
}
