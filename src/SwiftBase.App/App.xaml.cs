using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace SwiftBase.App;

public partial class App : Application
{
    private const long MaxLogBytes = 256 * 1024;

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SwiftBase",
        "crash.log");

    private static readonly Regex SecretPattern = new(
        @"(?i)\b(password|pwd|passphrase|secret|token|api[_-]?key)\b(\s*[=:]\s*)(?:""[^""\r\n]*""|'[^'\r\n]*'|[^\s;,]+)",
        RegexOptions.Compiled);

    private static readonly Regex UrlPasswordPattern = new(
        @"(?i)(://[^:/\s]+:)[^@\s/]+(@)",
        RegexOptions.Compiled);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log("Unhandled", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log("Unobserved task", args.Exception);
            args.SetObserved();
        };
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log("Dispatcher", e.Exception);
        var message = Sanitize(e.Exception.GetBaseException().Message);
        MessageBox.Show(
            "SwiftBase hit an unexpected error:\n\n" + Truncate(message, 600),
            "SwiftBase",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        e.Handled = true;
    }

    private static void Log(string source, Exception? exception)
    {
        if (exception is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            if (!File.Exists(LogPath))
            {
                using var created = new FileStream(LogPath, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite);
            }

            var info = new FileInfo(LogPath);
            if (info.Exists && info.Length > MaxLogBytes)
            {
                var archive = LogPath + ".old";
                if (File.Exists(archive))
                {
                    File.Delete(archive);
                }

                File.Move(LogPath, archive);
                using var rotated = new FileStream(LogPath, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite);
                RestrictToCurrentUser(archive);
            }

            // Apply the ACL after rotation so the newly created file is private too.
            RestrictToCurrentUser();

            var builder = new StringBuilder();
            builder.Append('[').Append(DateTime.Now.ToString("O")).Append("] ").Append(source).AppendLine();
            builder.AppendLine(Describe(exception));
            builder.AppendLine();
            File.AppendAllText(LogPath, builder.ToString());
        }
        catch
        {
            // Logging must never take the app down.
        }
    }

    private static string Describe(Exception exception)
    {
        var builder = new StringBuilder();
        builder.Append(exception.GetType().FullName).Append(": ")
            .AppendLine(Truncate(Sanitize(exception.GetBaseException().Message), 1500));

        var stack = exception.StackTrace;
        if (!string.IsNullOrWhiteSpace(stack))
        {
            var lines = stack.Split('\n').Select(line => line.TrimEnd('\r')).Take(15);
            builder.AppendLine(string.Join('\n', lines));
        }

        return builder.ToString();
    }

    private static string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        // Exception text can contain connection strings, URLs, SQL values or
        // payload fragments. Keep only what is needed to diagnose a crash.
        var redacted = SecretPattern.Replace(text, "$1$2***");
        redacted = UrlPasswordPattern.Replace(redacted, "$1***$2");
        return Truncate(redacted, 2000).Replace('\0', ' ');
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + " … [truncated]";

    private static void RestrictToCurrentUser(string? path = null)
    {
        try
        {
            var identity = WindowsIdentity.GetCurrent().User;
            if (identity is null)
            {
                return;
            }

            var security = new FileSecurity();
            security.SetAccessRuleProtection(true, false);
            security.SetOwner(identity);
            security.SetAccessRule(new FileSystemAccessRule(
                identity,
                FileSystemRights.FullControl,
                AccessControlType.Allow));
            new FileInfo(path ?? LogPath).SetAccessControl(security);
        }
        catch
        {
            // ACL hardening is best-effort on restricted file systems.
        }
    }
}
