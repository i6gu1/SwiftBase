using System.Diagnostics;
using System.IO;
using SwiftBase.Core.Models;
using SwiftBase.Core.Services;

namespace SwiftBase.ViewModels;

public sealed class ConnectionEditorViewModel : ObservableObject
{
    private DatabaseEngine _engine = DatabaseEngine.Sqlite;
    private string _name = "";
    private string _host = "127.0.0.1";
    private string _portText = "";
    private string _database = "";
    private string _username = "";
    private string _password = "";
    private string _environment = "Local";
    private bool _useSsh;
    private string _sshHost = "";
    private string _sshPortText = "22";
    private string _sshUsername = "";
    private string _sshPassword = "";
    private string _sshKeyPath = "";
    private string _sshPassphrase = "";
    private string _urlText = "";
    private string _importStatus = "";
    private bool _importSucceeded;
    private string _validationMessage = "";
    private string _testMessage = "";
    private bool _hasTestResult;
    private bool _testSucceeded;
    private bool _isTesting;

    public DatabaseEngine Engine
    {
        get => _engine;
        set
        {
            if (!Set(ref _engine, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsSqlite));
            OnPropertyChanged(nameof(IsServerBased));
            OnPropertyChanged(nameof(EngineName));
            OnPropertyChanged(nameof(EngineHint));
            if (string.IsNullOrWhiteSpace(PortText) || PortText == "5432" || PortText == "3306")
            {
                PortText = value switch
                {
                    DatabaseEngine.Postgres => "5432",
                    DatabaseEngine.MySql => "3306",
                    _ => "",
                };
            }

            ClearResult();
        }
    }

    public bool IsSqlite => Engine == DatabaseEngine.Sqlite;
    public bool IsServerBased => !IsSqlite;

    public string EngineName => Engine switch
    {
        DatabaseEngine.Postgres => "PostgreSQL",
        DatabaseEngine.MySql => "MySQL",
        _ => "SQLite",
    };

    public string EngineHint => Engine switch
    {
        DatabaseEngine.Postgres => "postgresql:// or postgres://",
        DatabaseEngine.MySql => "mysql:// or mariadb://",
        _ => "a local .sqlite or .db file",
    };

    public IReadOnlyList<string> Environments { get; } = new[] { "Local", "Staging", "Production" };

    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    public string Host
    {
        get => _host;
        set => Set(ref _host, value);
    }

    public string PortText
    {
        get => _portText;
        set => Set(ref _portText, value);
    }

    public string Database
    {
        get => _database;
        set => Set(ref _database, value);
    }

    public string Username
    {
        get => _username;
        set => Set(ref _username, value);
    }

    public string Password
    {
        get => _password;
        set => Set(ref _password, value);
    }

    public string Environment
    {
        get => _environment;
        set => Set(ref _environment, value);
    }

    public bool UseSsh
    {
        get => _useSsh;
        set => Set(ref _useSsh, value);
    }

    public string SshHost
    {
        get => _sshHost;
        set => Set(ref _sshHost, value);
    }

    public string SshPortText
    {
        get => _sshPortText;
        set => Set(ref _sshPortText, value);
    }

    public string SshUsername
    {
        get => _sshUsername;
        set => Set(ref _sshUsername, value);
    }

    public string SshPassword
    {
        get => _sshPassword;
        set => Set(ref _sshPassword, value);
    }

    public string SshKeyPath
    {
        get => _sshKeyPath;
        set => Set(ref _sshKeyPath, value);
    }

    public string SshPassphrase
    {
        get => _sshPassphrase;
        set => Set(ref _sshPassphrase, value);
    }

    public string UrlText
    {
        get => _urlText;
        set => Set(ref _urlText, value);
    }

    public string ImportStatus
    {
        get => _importStatus;
        private set
        {
            if (Set(ref _importStatus, value))
            {
                OnPropertyChanged(nameof(HasImportStatus));
            }
        }
    }

    public bool HasImportStatus => !string.IsNullOrWhiteSpace(ImportStatus);
    public bool ImportSucceeded => _importSucceeded;

    public string ValidationMessage
    {
        get => _validationMessage;
        private set
        {
            if (Set(ref _validationMessage, value))
            {
                OnPropertyChanged(nameof(HasValidationMessage));
            }
        }
    }

    public bool HasValidationMessage => !string.IsNullOrWhiteSpace(ValidationMessage);

    public string TestMessage
    {
        get => _testMessage;
        private set => Set(ref _testMessage, value);
    }

    public bool HasTestResult
    {
        get => _hasTestResult;
        private set => Set(ref _hasTestResult, value);
    }

    public bool TestSucceeded
    {
        get => _testSucceeded;
        private set => Set(ref _testSucceeded, value);
    }

    public bool IsTesting
    {
        get => _isTesting;
        private set => Set(ref _isTesting, value);
    }

    public void ImportUrl()
    {
        ValidationMessage = "";
        ClearResult();
        var raw = UrlText.Trim();
        if (raw.Length == 0)
        {
            SetImport("Paste a connection URL first.", false);
            return;
        }

        try
        {
            if (raw.StartsWith("sqlite:", StringComparison.OrdinalIgnoreCase))
            {
                var path = raw["sqlite:".Length..].TrimStart('/');
                if (raw.StartsWith("sqlite:///", StringComparison.OrdinalIgnoreCase))
                {
                    path = raw["sqlite:///".Length..];
                }

                path = Uri.UnescapeDataString(path).Replace('/', Path.DirectorySeparatorChar);
                Engine = DatabaseEngine.Sqlite;
                Database = path;
                Name = string.IsNullOrWhiteSpace(Name) ? Path.GetFileNameWithoutExtension(path) : Name;
                SetImport("SQLite path imported.", true);
                return;
            }

            if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
            {
                SetImport("That does not look like a valid connection URL.", false);
                return;
            }

            var engine = uri.Scheme.ToLowerInvariant() switch
            {
                "postgres" or "postgresql" => DatabaseEngine.Postgres,
                "mysql" or "mariadb" => DatabaseEngine.MySql,
                "sqlite" => DatabaseEngine.Sqlite,
                _ => (DatabaseEngine?)null,
            };

            if (engine is null)
            {
                SetImport("Unsupported URL scheme. Use postgres://, mysql:// or sqlite://.", false);
                return;
            }

            Engine = engine.Value;
            if (engine == DatabaseEngine.Sqlite)
            {
                var path = Uri.UnescapeDataString(uri.LocalPath).TrimStart('/', '\\');
                Database = path.Replace('/', Path.DirectorySeparatorChar);
                Name = string.IsNullOrWhiteSpace(Name) ? Path.GetFileNameWithoutExtension(path) : Name;
                SetImport("SQLite path imported.", true);
                return;
            }

            Host = uri.Host;
            if (!uri.IsDefaultPort && uri.Port > 0)
            {
                PortText = uri.Port.ToString();
            }
            else if (string.IsNullOrWhiteSpace(PortText))
            {
                PortText = engine == DatabaseEngine.Postgres ? "5432" : "3306";
            }

            Database = Uri.UnescapeDataString(uri.AbsolutePath.Trim('/'));

            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                var parts = uri.UserInfo.Split(':', 2);
                Username = Uri.UnescapeDataString(parts[0]);
                Password = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "";
            }

            Name = string.IsNullOrWhiteSpace(Name) ? Database : Name;
            SetImport($"{EngineName} URL imported.", true);
        }
        catch (Exception ex)
        {
            SetImport("Could not read the URL: " + ex.Message, false);
        }
    }

    public bool Validate()
    {
        ValidationMessage = "";
        if (IsSqlite)
        {
            if (string.IsNullOrWhiteSpace(Database))
            {
                ValidationMessage = "Choose a SQLite database file first.";
                return false;
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(Host))
            {
                ValidationMessage = "Host is required.";
                return false;
            }

            if (!int.TryParse(PortText, out var port) || port is < 1 or > 65535)
            {
                ValidationMessage = "Port must be between 1 and 65535.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(Database))
            {
                ValidationMessage = "Database name is required.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(Username))
            {
                ValidationMessage = "Username is required.";
                return false;
            }
        }

        if (UseSsh && IsServerBased)
        {
            if (string.IsNullOrWhiteSpace(SshHost) || string.IsNullOrWhiteSpace(SshUsername))
            {
                ValidationMessage = "SSH host and username are required.";
                return false;
            }

            if (!int.TryParse(SshPortText, out var sshPort) || sshPort is < 1 or > 65535)
            {
                ValidationMessage = "SSH port must be between 1 and 65535.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(SshPassword) && string.IsNullOrWhiteSpace(SshKeyPath))
            {
                ValidationMessage = "Provide an SSH password or a private key path.";
                return false;
            }
        }

        return true;
    }

    public ConnectionConfig CreateConfig()
    {
        var name = string.IsNullOrWhiteSpace(Name)
            ? (IsSqlite ? Path.GetFileNameWithoutExtension(Database) : Database)
            : Name.Trim();

        int.TryParse(PortText, out var port);
        int.TryParse(SshPortText, out var sshPort);

        return new ConnectionConfig
        {
            Name = name,
            Engine = Engine,
            Host = IsSqlite ? "" : Host.Trim(),
            Port = IsSqlite ? 0 : port,
            Database = Database.Trim(),
            Username = Username.Trim(),
            Password = Password,
            Environment = Environment,
            UseSsh = UseSsh && IsServerBased,
            Ssh = new SshConfig
            {
                Host = SshHost.Trim(),
                Port = sshPort <= 0 ? 22 : sshPort,
                Username = SshUsername.Trim(),
                Password = SshPassword,
                PrivateKeyPath = string.IsNullOrWhiteSpace(SshKeyPath) ? null : SshKeyPath.Trim(),
                Passphrase = SshPassphrase,
            },
        };
    }

    public async Task TestConnectionAsync()
    {
        if (IsTesting)
        {
            return;
        }

        if (!Validate())
        {
            return;
        }

        IsTesting = true;
        ClearResult();
        var stopwatch = Stopwatch.StartNew();
        DatabaseSession? session = null;
        try
        {
            var config = CreateConfig();
            session = await DatabaseSession.OpenAsync(config, config.Database);
            var result = await session.Provider.TestAsync();
            stopwatch.Stop();
            TestSucceeded = result.Success;
            HasTestResult = true;
            TestMessage = result.Success
                ? $"Connected successfully · {result.ElapsedMs} ms"
                : "Connection failed: " + result.Message;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            TestSucceeded = false;
            HasTestResult = true;
            TestMessage = $"Connection failed after {stopwatch.ElapsedMilliseconds} ms: {ex.GetBaseException().Message}";
        }
        finally
        {
            if (session is not null)
            {
                await session.DisposeAsync();
            }

            IsTesting = false;
        }
    }

    private void SetImport(string message, bool success)
    {
        _importSucceeded = success;
        ImportStatus = message;
        OnPropertyChanged(nameof(ImportSucceeded));
    }

    private void ClearResult()
    {
        HasTestResult = false;
        TestSucceeded = false;
        TestMessage = "";
    }
}
