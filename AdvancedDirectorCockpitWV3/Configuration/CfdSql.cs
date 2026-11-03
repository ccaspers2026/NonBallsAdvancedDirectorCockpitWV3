using System.Windows;
using AdvancedDirectorCockpitWV3.Services;

namespace AdvancedDirectorCockpitWV3.Configuration;

// Shell facade (unchanged static API). The CfdStartupService instance comes from the host (App.OnStartup);
// its connection string is ConnectionStrings:Cfd from appsettings.{Environment}.json.
// Shell -> Services (CfdStartupService) -> Data.
internal static class CfdSql
{
    internal static CfdStartupService? Service { get; set; }

    public static bool Connected => Service?.Connected ?? false;
    public static string Status => Service?.Status ?? "CFD SQL: not configured (ConnectionStrings:Cfd)";

    public static bool Verify() => Service?.Verify() ?? false;

    public static void VerifyOnStartup()
    {
        if (!Verify())
            MessageBox.Show(Status + "\n\nThe cockpit will continue without the CFD database.",
                "Advanced Director Cockpit WV3 - CFD database", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}