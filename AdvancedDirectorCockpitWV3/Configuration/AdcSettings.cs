using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AdvancedDirectorCockpitWV3.Configuration;

// 2026-10-31 appsettings refactor. Bound from appsettings.json + appsettings.{Environment}.json (+ env vars).
// Section "Cockpit".
public sealed class CockpitOptions
{
    public const string Section = "Cockpit";
    [Required] public string VirtualHost { get; set; } = "adc-wv2.local";
    [Range(1, 64)] public int MaxSlots { get; set; } = 16;
    // Single data-folder setting. Every instance reads and saves cards/groups/tabs/stages here.
    [Required] public string DataDir { get; set; } = "";
    [Required] public string PubExePath { get; set; } = "";
}

// Section "Vault": in-process vault (Vault\AdcVault.cs). 2026-11-02: no Python frame proxy, no port.
public sealed class VaultOptions
{
    public const string Section = "Vault";
    // Folder for vault.dat (+ pending-hosts.json). Empty = %LOCALAPPDATA%\AdvancedDirectorCockpitWV3\vault (per Windows user).
    public string StoreDir { get; set; } = "";
    // Optional one-time import: copied (never moved) into StoreDir only when StoreDir has no vault.dat yet.
    public string ImportFrom { get; set; } = "";
    [Range(1024, 1048576)] public int MaxBodyBytes { get; set; } = 16384;
}

// Values are copied once from the host in App.OnStartup (least-invasive bridge to the static MainWindow code).
internal static class AdcSettings
{
    public static CockpitOptions Cockpit { get; set; } = new();
    public static VaultOptions Vault { get; set; } = new();

    public static IServiceCollection AddAdcCockpit(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<CockpitOptions>().Bind(config.GetSection(CockpitOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<VaultOptions>().Bind(config.GetSection(VaultOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
        return services;
    }
}