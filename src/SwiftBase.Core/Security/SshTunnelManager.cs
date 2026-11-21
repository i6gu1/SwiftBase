using Renci.SshNet;
using SwiftBase.Core.Models;

namespace SwiftBase.Core.Security;

/// <summary>Loops a local port through an SSH server for remote databases.</summary>
public sealed class SshTunnelManager : IDisposable
{
    private readonly SshClient _client;
    private readonly ForwardedPortLocal _port;
    private bool _disposed;

    private SshTunnelManager(SshClient client, ForwardedPortLocal port, int localPort)
    {
        _client = client;
        _port = port;
        LocalPort = localPort;
    }

    public int LocalPort { get; }

    public static async Task<SshTunnelManager> StartAsync(
        SshConfig config,
        string targetHost,
        int targetPort,
        CancellationToken cancellationToken = default)
    {
        if (!config.IsConfigured)
        {
            throw new InvalidOperationException("SSH tunnel is enabled but the SSH host or username is empty.");
        }

        var host = config.Host.Trim();
        var port = config.Port <= 0 ? 22 : config.Port;
        var hostKeys = new SshHostKeyStore();
        SshClient? client = null;
        string? hostKeyFailure = null;

        // Task.Run's token only cancels before the delegate starts. Disposing the
        // in-flight client is what actually aborts a connect that is already running.
        using var cancellation = cancellationToken.Register(() => client?.Dispose());

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var methods = new List<AuthenticationMethod>();
            if (!string.IsNullOrWhiteSpace(config.PrivateKeyPath) && File.Exists(config.PrivateKeyPath))
            {
                var key = string.IsNullOrEmpty(config.Passphrase)
                    ? new PrivateKeyFile(config.PrivateKeyPath)
                    : new PrivateKeyFile(config.PrivateKeyPath, config.Passphrase);
                methods.Add(new PrivateKeyAuthenticationMethod(config.Username, key));
            }

            methods.Add(new PasswordAuthenticationMethod(config.Username, config.Password ?? ""));

            var connectionInfo = new ConnectionInfo(host, port, config.Username, methods.ToArray())
            {
                Timeout = TimeSpan.FromSeconds(15),
            };

            client = new SshClient(connectionInfo);
            client.HostKeyReceived += (_, args) =>
            {
                var fingerprint = args.FingerPrintSHA256 ?? "";
                var verification = hostKeys.Verify(host, port, fingerprint);
                if (verification == SshHostKeyVerification.Trusted)
                {
                    args.CanTrust = true;
                    return;
                }

                if (verification == SshHostKeyVerification.Mismatch)
                {
                    hostKeyFailure = $"SSH host key changed for {host}:{port}. Refusing to connect.";
                    args.CanTrust = false;
                    return;
                }

                if (verification == SshHostKeyVerification.LoadFailed)
                {
                    hostKeyFailure = "SSH known-hosts file could not be read. Refusing to connect.";
                    args.CanTrust = false;
                    return;
                }

                try
                {
                    // Trust on first use; every later connection must match.
                    hostKeys.Trust(host, port, fingerprint);
                    args.CanTrust = true;
                }
                catch (Exception ex)
                {
                    hostKeyFailure = "SSH host key could not be verified: " + ex.Message;
                    args.CanTrust = false;
                }
            };
            client.KeepAliveInterval = TimeSpan.FromSeconds(30);

            try
            {
                client.Connect();
                cancellationToken.ThrowIfCancellationRequested();
                if (hostKeyFailure is not null)
                {
                    throw new InvalidOperationException(hostKeyFailure);
                }

                if (!client.IsConnected)
                {
                    throw new InvalidOperationException("The SSH server refused the connection.");
                }

                // boundPort 0 lets the OS choose a free port while the listener is
                // held, avoiding a race between port discovery and forwarding.
                var forwarded = new ForwardedPortLocal("127.0.0.1", 0, targetHost, (uint)targetPort);
                client.AddForwardedPort(forwarded);
                forwarded.Start();
                cancellationToken.ThrowIfCancellationRequested();
                var tunnel = new SshTunnelManager(client, forwarded, (int)forwarded.BoundPort);

                // The tunnel owns the client now; do not let a late cancellation
                // callback dispose the client we just handed to the caller.
                client = null;
                return tunnel;
            }
            catch
            {
                client?.Dispose();
                throw;
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _port.Stop();
        }
        catch
        {
            // ignored
        }

        try
        {
            _client.Disconnect();
        }
        catch
        {
            // ignored
        }

        _client.Dispose();
    }
}
