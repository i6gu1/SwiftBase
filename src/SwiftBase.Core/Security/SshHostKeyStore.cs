using System.Text.Json;

namespace SwiftBase.Core.Security;

public enum SshHostKeyVerification
{
    Unknown,
    Trusted,
    Mismatch,
    LoadFailed,
}

/// <summary>
/// Persists SSH host-key fingerprints using trust-on-first-use. Once a key is
/// stored for a host:port, a different key fails closed.
/// </summary>
public sealed class SshHostKeyStore
{
    private readonly object _sync = new();
    private readonly string _path;
    private bool _loadFailed;

    public SshHostKeyStore(string? path = null)
    {
        _path = path ?? Path.Combine(AppPaths.DataDirectory, "ssh_known_hosts.json");
    }

    public string FilePath => _path;

    public SshHostKeyVerification Verify(string host, int port, string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            return SshHostKeyVerification.Mismatch;
        }

        lock (_sync)
        {
            var keys = LoadKeys();
            if (_loadFailed)
            {
                return SshHostKeyVerification.LoadFailed;
            }

            if (!keys.TryGetValue(Key(host, port), out var stored) || stored.Count == 0)
            {
                return SshHostKeyVerification.Unknown;
            }

            return stored.Any(value => string.Equals(value, fingerprint, StringComparison.OrdinalIgnoreCase))
                ? SshHostKeyVerification.Trusted
                : SshHostKeyVerification.Mismatch;
        }
    }

    public bool IsKnown(string host, int port, string fingerprint) =>
        Verify(host, port, fingerprint) == SshHostKeyVerification.Trusted;

    /// <summary>Stores a first-use key. Never updates an existing, different key.</summary>
    public void Trust(string host, int port, string fingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);

        lock (_sync)
        {
            var keys = LoadKeys();
            if (_loadFailed)
            {
                throw new InvalidOperationException(
                    "The SSH known-hosts file could not be read. Refusing to overwrite it.");
            }

            var key = Key(host, port);
            if (!keys.TryGetValue(key, out var stored))
            {
                stored = new List<string>();
                keys[key] = stored;
            }

            if (stored.Any(value => string.Equals(value, fingerprint, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            if (stored.Count > 0)
            {
                throw new InvalidOperationException(
                    "The SSH host key changed. Refusing to replace the stored fingerprint.");
            }

            stored.Add(fingerprint);
            SaveKeys(keys);
        }
    }

    private Dictionary<string, List<string>> LoadKeys()
    {
        _loadFailed = false;
        var empty = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(_path))
        {
            return empty;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(_path));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                _loadFailed = true;
                return empty;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                var fingerprints = new List<string>();
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    var value = property.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        fingerprints.Add(value);
                    }
                }
                else if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in property.Value.EnumerateArray())
                    {
                        if (element.ValueKind == JsonValueKind.String &&
                            element.GetString() is { } value &&
                            !string.IsNullOrWhiteSpace(value))
                        {
                            fingerprints.Add(value);
                        }
                    }
                }

                if (fingerprints.Count > 0)
                {
                    empty[property.Name] = fingerprints;
                }
            }

            return empty;
        }
        catch
        {
            _loadFailed = true;
            return empty;
        }
    }

    private void SaveKeys(Dictionary<string, List<string>> keys)
    {
        var directory = Path.GetDirectoryName(_path);
        if (string.IsNullOrEmpty(directory))
        {
            directory = AppPaths.DataDirectory;
        }

        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, $"ssh_known_hosts.{Guid.NewGuid():N}.tmp");
        try
        {
            var payload = keys.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToArray(),
                StringComparer.OrdinalIgnoreCase);
            File.WriteAllText(temp, JsonSerializer.Serialize(payload));
            File.Move(temp, _path, true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    private static string Key(string host, int port) =>
        $"{host.Trim().ToLowerInvariant()}:{port}";
}
