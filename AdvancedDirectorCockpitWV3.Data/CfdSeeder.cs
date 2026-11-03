using System.Reflection;
using DbUp;

namespace AdvancedDirectorCockpitWV3.Data;

// dbUp seed data (Seeds\*.sql, embedded). Journal: dbo.SchemaVersions_Seed. Separate from EF schema migrations.
public static class CfdSeeder
{
    public static (bool ok, string message) Run(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var upgrader = DeployChanges.To
            .SqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(Assembly.GetExecutingAssembly(), s => s.Contains(".Seeds.", StringComparison.Ordinal))
            .JournalToSqlTable("dbo", "SchemaVersions_Seed")
            .LogToNowhere()
            .Build();
        if (!upgrader.IsUpgradeRequired()) return (true, "no seeds pending");
        var r = upgrader.PerformUpgrade();
        return (r.Successful, r.Successful ? $"applied {r.Scripts.Count()} seed script(s)" : r.Error?.Message ?? "seed failed");
    }
}