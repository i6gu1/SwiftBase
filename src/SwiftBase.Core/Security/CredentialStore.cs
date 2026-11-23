using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SwiftBase.Core.Models;

namespace SwiftBase.Core.Security;

/// <summary>
/// Stores non-secret connection fields in JSON and protects every secret with
/// Windows DPAPI (CurrentUser), so only this Windows account can decrypt it.
/// </summary>
public sealed class CredentialStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SwiftBase.Vault.v1");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private bool _loadFailed;
    private bool _decryptFailed;

    public CredentialStore(string? path = null)
    {
        _path = path ?? AppPaths.ConnectionsFile;
    }

    public string FilePath => _path;

    /// <summary>True when the vault file exists but could not be read or parsed.</summary>
    public bool LastLoadFailed => _loadFailed;

    /// <summary>True when at least one DPAPI-protected secret could not be decrypted.</summary>
    public bool LastDecryptFailed => _decryptFailed;

    public IReadOnlyList<ConnectionConfig> Load()
    {
        _loadFailed = false;
        _decryptFailed = false;
        if (!File.Exists(_path))
        {
            return Array.Empty<ConnectionConfig>();
        }

        try
        {
            var json = File.ReadAllText(_path, Encoding.UTF8);
            var stored = JsonSerializer.Deserialize<List<StoredConnection>>(json, JsonOptions);
            return stored is null
                ? Array.Empty<ConnectionConfig>()
                : stored.Select(ToConfig).ToList();
        }
        catch
        {
            // Keep the data on disk. Callers must not overwrite it with an
            // empty list after a transient read/parse failure.
            _loadFailed = true;
            return Array.Empty<ConnectionConfig>();
        }
    }

    public void SaveAll(IEnumerable<ConnectionConfig> configs)
    {
        if (_loadFailed || _decryptFailed)
        {
            throw new InvalidOperationException(
                "Saved connections or their protected secrets could not be read. " +
                "Refusing to overwrite the vault. Re-enter the affected credentials before saving.");
        }

        List<StoredConnection> stored;
        try
        {
            stored = configs.Select(ToStored).ToList();
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException(
                "Windows could not protect the connection secrets: " + ex.Message, ex);
        }

        var directory = Path.GetDirectoryName(_path);
        if (string.IsNullOrEmpty(directory))
        {
            directory = AppPaths.DataDirectory;
        }

        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(stored, JsonOptions);
        var temp = Path.Combine(directory, $"connections.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temp, json, new UTF8Encoding(false));

            // Verify the serialized payload before replacing the live vault.
            if (JsonSerializer.Deserialize<List<StoredConnection>>(
                    File.ReadAllText(temp, Encoding.UTF8), JsonOptions) is null)
            {
                throw new InvalidOperationException("The generated vault file was empty.");
            }

            File.Move(temp, _path, true);

            // The payload is verified before the atomic replace, so a persistent
            // backup is unnecessary. Remove one left by an older build so stale
            // protected secrets do not remain on disk indefinitely.
            var staleBackup = _path + ".bak";
            if (File.Exists(staleBackup))
            {
                File.Delete(staleBackup);
            }
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    public void Upsert(ConnectionConfig config)
    {
        var configs = Load().ToList();
        var index = configs.FindIndex(c => c.Id == config.Id);
        if (index >= 0)
        {
            configs[index] = config;
        }
        else
        {
            configs.Add(config);
        }

        SaveAll(configs);
    }

    public void Remove(Guid id)
    {
        var configs = Load().Where(c => c.Id != id).ToList();
        SaveAll(configs);
    }

    private static StoredConnection ToStored(ConnectionConfig config) => new()
    {
        Id = config.Id,
        Name = config.Name,
        Engine = config.Engine,
        Host = config.Host,
        Port = config.Port,
        Database = config.Database,
        Username = config.Username,
        Environment = config.Environment,
        Password = Protect(config.Password),
        UseSsh = config.UseSsh,
        AllowUnverifiedTls = config.AllowUnverifiedTls,
        RootCertificatePath = config.RootCertificatePath,
        SshHost = config.Ssh.Host,
        SshPort = config.Ssh.Port,
        SshUsername = config.Ssh.Username,
        SshPassword = Protect(config.Ssh.Password),
        SshTrustNewHostKey = config.Ssh.TrustNewHostKey,
        SshPrivateKeyPath = config.Ssh.PrivateKeyPath,
        SshPassphrase = Protect(config.Ssh.Passphrase),
    };

    private ConnectionConfig ToConfig(StoredConnection stored) => new()
    {
        Id = stored.Id,
        Name = stored.Name,
        Engine = stored.Engine,
        Host = stored.Host,
        Port = stored.Port,
        Database = stored.Database,
        Username = stored.Username,
        Environment = string.IsNullOrWhiteSpace(stored.Environment) ? "Local" : stored.Environment,
        Password = Unprotect(stored.Password),
        UseSsh = stored.UseSsh,
        AllowUnverifiedTls = stored.AllowUnverifiedTls,
        RootCertificatePath = stored.RootCertificatePath,
        Ssh = new SshConfig
        {
            Host = stored.SshHost ?? "",
            Port = stored.SshPort <= 0 ? 22 : stored.SshPort,
            Username = stored.SshUsername ?? "",
            Password = Unprotect(stored.SshPassword),
            PrivateKeyPath = stored.SshPrivateKeyPath,
            Passphrase = Unprotect(stored.SshPassphrase),
            TrustNewHostKey = stored.SshTrustNewHostKey,
        },
    };

    private static string? Protect(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser);
        return "dpapi:" + Convert.ToBase64String(bytes);
    }

    private string? Unprotect(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        try
        {
            if (!value.StartsWith("dpapi:", StringComparison.Ordinal))
            {
                _decryptFailed = true;
                return null;
            }

            var bytes = Convert.FromBase64String(value["dpapi:".Length..]);
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.CurrentUser));
        }
        catch
        {
            // A different Windows account, a migrated profile or corrupt ciphertext:
            // keep the data on disk and block any save that would erase it.
            _decryptFailed = true;
            return null;
        }
    }

    private sealed class StoredConnection
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public DatabaseEngine Engine { get; set; }
        public string Host { get; set; } = "";
        public int Port { get; set; }
        public string Database { get; set; } = "";
        public string Username { get; set; } = "";
        public string Environment { get; set; } = "Local";
        public string? Password { get; set; }
        public bool UseSsh { get; set; }
        public bool AllowUnverifiedTls { get; set; }
        public string? RootCertificatePath { get; set; }
        public bool SshTrustNewHostKey { get; set; }
        public string? SshHost { get; set; }
        public int SshPort { get; set; }
        public string? SshUsername { get; set; }
        public string? SshPassword { get; set; }
        public string? SshPrivateKeyPath { get; set; }
        public string? SshPassphrase { get; set; }
    }
}
