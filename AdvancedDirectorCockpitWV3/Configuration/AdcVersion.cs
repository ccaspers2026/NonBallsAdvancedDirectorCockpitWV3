using System.Reflection;
using System.Text.Json;

namespace AdvancedDirectorCockpitWV3.Configuration
{
    // 2026-10-09 build version (adc-version). The csproj sets InformationalVersion = VersionPrefix + "+" + yyyyMMdd.HHmm
    // on every build, plus an optional AdcRelease tag (e.g. r4). Nothing here needs editing per build.
    internal static class AdcVersion
    {
        public const string Product = "Advanced Director Cockpit WV3";
        public static readonly string Version;
        public static readonly string Release;
        public static readonly string Display;

        static AdcVersion()
        {
            var asm = typeof(AdcVersion).Assembly;
            Version = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? asm.GetName().Version?.ToString() ?? "0.0.0";
            Release = asm.GetCustomAttributes<AssemblyMetadataAttribute>()
                          .FirstOrDefault(a => a.Key == "AdcRelease")?.Value ?? "";
            Display = "v" + Version + (string.IsNullOrWhiteSpace(Release) ? "" : " (" + Release.Trim() + ")");
        }

        // "Advanced Director Cockpit WV3 - Window 2" -> "Advanced Director Cockpit WV3 v1.4.0+20261009.2215 (r4) - Window 2";
        // other titles (detached tabs "ADC - label") get " - v..." appended. Idempotent.
        public static string Titled(string? title)
        {
            if (string.IsNullOrWhiteSpace(title)) return Product + " " + Display;
            if (title.Contains(Display, StringComparison.Ordinal)) return title;
            if (title.StartsWith(Product, StringComparison.Ordinal))
                return Product + " " + Display + (CfdSql.Connected ? " [CFD local]" : " [CFD offline]") + title.Substring(Product.Length);
            return title + " - " + Display;
        }

        // Runs before the page's own scripts on every document: the page reads window.__adcHostVersion for its label.
        public static string PageScript()
        {
            var json = JsonSerializer.Serialize(new { version = Version, release = Release, display = Display });
            return "window.__adcHostVersion=" + json + ";";
        }
    }
}
