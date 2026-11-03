using AdvancedDirectorCockpitWV3.Data;
using AdvancedDirectorCockpitWV3.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AdvancedDirectorCockpitWV3.Data.Tests;

public class SeederAndMigrationTests
{
    [Fact] public void Empty_db_has_zero_user_tables()
    {
        using var t = new TempDb();
        Assert.Equal(0, new CfdProbeRepository(t.ConnectionString).UserTableCount());
    }

    [Fact] public void Migrate_creates_cfd_and_board_tables_and_probe_works()
    {
        using var t = new TempDb();
        using (var db = CfdDbContext.Create(t.ConnectionString)) db.Database.Migrate();
        var repo = new CfdProbeRepository(t.ConnectionString);
        Assert.Equal(12, repo.UserTableCount()); // 5 CFD tables + 6 Board* tables + __EFMigrationsHistory
        Assert.Equal(0, repo.Probe().LoanCount);
        Assert.Empty(new LoanRepository(t.ConnectionString).GetAll());
    }

    [Fact] public void Seeder_after_migrate_journals_to_SchemaVersions_Seed_and_is_idempotent()
    {
        using var t = new TempDb();
        using (var db = CfdDbContext.Create(t.ConnectionString)) db.Database.Migrate();
        var (ok1, m1) = CfdSeeder.Run(t.ConnectionString);
        Assert.True(ok1, m1);
        Assert.StartsWith("applied", m1);
        Assert.Equal(3, t.Scalar("SELECT COUNT(*) FROM dbo.SchemaVersions_Seed"));
        var (ok2, m2) = CfdSeeder.Run(t.ConnectionString);
        Assert.True(ok2);
        Assert.Equal("no seeds pending", m2);
        Assert.Equal(3, t.Scalar("SELECT COUNT(*) FROM dbo.SchemaVersions_Seed"));
    }
}
