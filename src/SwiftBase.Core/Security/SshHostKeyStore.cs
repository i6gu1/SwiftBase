using System.Security.Cryptography;
using System.Text;
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
/// Persists SSH host-key fingerprints. Verification and first-use trust are one
/// atomic operation guarded by a process-wide lock and a named mutex, so two
/// connections or two app instances cannot both "win" the first connection.
/// A key is never trusted unless the caller explicitly allows first use.
/// </summary>
public sealed class SshHostKeyStore
{
    private static readonly object GatesLock = new();
    private static readonly Dictionary<string, object> Gates = new(StringComparer.OrdinalIgnoreCase);

    private readonly string _path;
    private readonly object _gate;

    public SshHostKeyStore(string? path = null)
    {
        _path = Path.GetFullPath(path ?? Path.Combine(AppPaths.DataDirectory, "ssh_known_hosts.json"));
        lock (GatesLock)
        {
            if (!Gates.TryGetValue(_path, out var gate))
            {
                gate = new object();
                Gates[_path] = gate;
            }

            _gate = gate;
        }
    }

    public string FilePath => _path;

    /// <summary>Read-only check. Unknown keys are never trusted implicitly.</summary>
    public SshHostKeyVerification Verify(string host, int port, string fingerprint) =>
        VerifyOrTrust(host, port, fingerprint, allowFirstUse: false, out _);

    public bool IsKnown(string host, int port, string fingerprint) =>
        Verify(host, port, fingerprint) == SshHostKeyVerification.Trusted;

    /// <summary>
    /// Verifies the fingerprint and, only when explicitly permitted, stores a new
    /// one. Returns a human-readable reason for every rejection.
    /// </summary>
    public SshHostKeyVerification VerifyOrTrust(
        string host,
        int port,
        string fingerprint,
        bool allowFirstUse,
        out string? failure)
    {
        failure = null;
        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            failure = "The SSH server did not provide a host-key fingerprint.";
            return SshHostKeyVerification.Mismatch;
        }

        var mutexName = "Local\\SwiftBase.SshHostKeys." +
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_path)));
        using var mutex = new Mutex(false, mutexName);
        var acquired = false;
        try
        {
            try
            {
                mutex.WaitOne();
            }
            catch (AbandonedMutexException)
            {
                // The previous owner died; this instance now owns the mutex.
            }

            acquired = true;
            lock (_gate)
            {
                var keys = LoadKeys(out var loadFailed);
                if (loadFailed)
                {
                    failure = "The SSH known-hosts file could not be read. Refusing to connect.";
                    return SshHostKeyVerification.LoadFailed;
                }

                var key = Key(host, port);
                if (!keys.TryGetValue(key, out var stored) || stored.Count == 0)
                {
                    if (!allowFirstUse)
                    {
                        failure =
                            $"Unknown SSH host key for {host}:{port} ({fingerprint}). " +
                            "Verify the fingerprint out of band, then enable 'Trust this host key on first use' for this connection.";
                        return SshHostKeyVerification.Unknown;
                    }

                    keys[key] = new List<string> { fingerprint };
                    SaveKeys(keys);
                    return SshHostKeyVerification.Trusted;
                }

                if (stored.Any(value => string.Equals(value, fingerprint, StringComparison.OrdinalIgnoreCase)))
                {
                    return SshHostKeyVerification.Trusted;
                }

                failure =
                    $"SSH host key changed for {host}:{port}. Expected {stored[0]}, received {fingerprint}. " +
                    "Refusing to connect.";
                return SshHostKeyVerification.Mismatch;
            }
        }
        finally
        {
            if (acquired)
            {
                mutex.ReleaseMutex();
            }
        }
    }

    /// <summary>Explicitly stores a first-use key. Never replaces a different key.</summary>
    public SshHostKeyVerification Trust(string host, int port, string fingerprint) =>
        VerifyOrTrust(host, port, fingerprint, allowFirstUse: true, out _);

    private Dictionary<string, List<string>> LoadKeys(out bool loadFailed)
    {
        loadFailed = false;
        var keys = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(_path))
        {
            return keys;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(_path));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                loadFailed = true;
                return keys;
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
                    keys[KeyFromStored(property.Name)] = fingerprints;
                }
            }

            return keys;
        }
        catch
        {
            loadFailed = true;
            return keys;
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

    // Stored keys are already canonical "host:port" values.
    private static string KeyFromStored(string key) => key;

    private static string Key(string host, int port) =>
        $"{host.Trim().ToLowerInvariant()}:{port}";
}
