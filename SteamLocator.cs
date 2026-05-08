using Microsoft.Win32;

namespace SteamPlaylogViewer;

public static class SteamLocator
{
    public static readonly string DefaultLogsDir = @"C:\Program Files (x86)\Steam\logs";
    public static readonly string DefaultLogPath = System.IO.Path.Combine(DefaultLogsDir, "gameprocess_log.txt");

    public static string ResolveLogPath()
    {
        var env = Environment.GetEnvironmentVariable("STEAM_LOG_PATH");
        if (!string.IsNullOrWhiteSpace(env))
            return Normalize(env);

        if (System.IO.File.Exists(DefaultLogPath))
            return DefaultLogPath;

        var steamDir = TryGetSteamInstallDirFromRegistry();
        if (!string.IsNullOrWhiteSpace(steamDir))
        {
            steamDir = Normalize(steamDir);
            return System.IO.Path.Combine(steamDir, "logs", "gameprocess_log.txt");
        }

        return DefaultLogPath;
    }

    private static string Normalize(string p) => (p ?? "").Trim().Trim('"').Replace('/', '\\');

    private static string? TryGetSteamInstallDirFromRegistry()
    {
        static string? FromRoot(RegistryKey root)
        {
            try
            {
                using var key = root.OpenSubKey(@"Software\Valve\Steam");
                if (key == null) return null;

                var steamPath = key.GetValue("SteamPath") as string;
                if (!string.IsNullOrWhiteSpace(steamPath)) return steamPath;

                var steamExe = key.GetValue("SteamExe") as string;
                if (!string.IsNullOrWhiteSpace(steamExe)) return System.IO.Path.GetDirectoryName(steamExe);

                return null;
            }
            catch { return null; }
        }

        return FromRoot(Registry.CurrentUser) ?? FromRoot(Registry.LocalMachine);
    }
}
