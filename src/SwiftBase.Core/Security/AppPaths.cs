namespace SwiftBase.Core.Security;

public static class AppPaths
{
    public static string DataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SwiftBase");

    public static string ConnectionsFile => Path.Combine(DataDirectory, "connections.json");

    public static string QueryHistoryFile => Path.Combine(DataDirectory, "queries.json");

    public static void EnsureDataDirectory() => Directory.CreateDirectory(DataDirectory);
}
