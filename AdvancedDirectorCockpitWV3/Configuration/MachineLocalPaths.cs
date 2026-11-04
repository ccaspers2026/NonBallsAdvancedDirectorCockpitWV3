using System.IO;

namespace AdvancedDirectorCockpitWV3.Configuration;

// 2026-11-04: Cockpit:PubExePath and Vault:ImportFrom are optional and machine-local.
// Empty stays empty (callers no-op). A relative value is resolved next to the running app.
// %ENV% tokens are expanded. Nothing here invents a drive letter.
// Env overrides (generic host, last wins): Cockpit__PubExePath, Vault__ImportFrom.
// PubExePath does not launch an exe. It names the published build for the window-title
// freshness check ([Pub] vs older-than-Pub) and for FindWww's last-resort page lookup.
// ImportFrom is the optional one-time vault.dat copy (AdcVault.ImportIfMissing).
internal static class MachineLocalPaths
{
    public const string PubExeFileName = "AdvancedDirectorCockpitWV3.exe";

    public static string ResolveConfigured(string? configured, string appDirectory)
    {
        if (string.IsNullOrWhiteSpace(configured)) return "";
        var expanded = Environment.ExpandEnvironmentVariables(configured.Trim());
        if (string.IsNullOrWhiteSpace(expanded)) return "";
        try
        {
            var root = string.IsNullOrWhiteSpace(appDirectory) ? "." : appDirectory;
            var full = Path.IsPathRooted(expanded) ? expanded : Path.Combine(root, expanded);
            return Path.GetFullPath(full);
        }
        catch
        {
            return "";
        }
    }

    // 1. Configured path (appsettings or Cockpit__PubExePath). Relative paths resolve next to the app.
    // 2. Else, if this process is itself the publish output (folder name "publish"), that exe — so [Pub] needs no config.
    // 3. Else "" — freshness compare and the pub www fallback no-op. A sibling publish folder is not implied.
    public static string ResolvePubExe(string? configured, string appDirectory, string? processPath)
    {
        var set = ResolveConfigured(configured, appDirectory);
        if (set.Length > 0) return set;
        if (IsPublishOutput(processPath))
        {
            try { return Path.GetFullPath(processPath!); }
            catch { return ""; }
        }
        return "";
    }

    public static bool IsPublishOutput(string? processPath)
    {
        if (string.IsNullOrWhiteSpace(processPath)) return false;
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(processPath));
            return string.Equals(Path.GetFileName(dir), "publish", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
