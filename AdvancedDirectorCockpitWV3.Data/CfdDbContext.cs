using AdvancedDirectorCockpitWV3.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace AdvancedDirectorCockpitWV3.Data;

// Mirrors the existing CFD schema (five tables) exactly; InitialCreate migration = that schema.
public class CfdDbContext : DbContext
{
    public CfdDbContext(DbContextOptions<CfdDbContext> options) : base(options) { }

    public DbSet<SchemaMeta> SchemaMeta => Set<SchemaMeta>();
    public DbSet<ConfigDocument> ConfigDocument => Set<ConfigDocument>();
    public DbSet<ConfigKey> ConfigKey => Set<ConfigKey>();
    public DbSet<Loan> Loan => Set<Loan>();
    public DbSet<Connection> Connection => Set<Connection>();

    public static CfdDbContext Create(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return new(new DbContextOptionsBuilder<CfdDbContext>().UseSqlServer(connectionString).Options);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        const string now = "sysdatetimeoffset()";
        b.Entity<SchemaMeta>(e => { e.ToTable("SchemaMeta", "dbo"); e.HasKey(x => x.Version); e.Property(x => x.Version).ValueGeneratedNever();
            e.Property(x => x.AppliedAt).HasDefaultValueSql(now); e.Property(x => x.Notes).HasMaxLength(400); });
        b.Entity<ConfigDocument>(e => { e.ToTable("ConfigDocument", "dbo"); e.HasKey(x => x.Id);
            e.Property(x => x.SavedAt).HasMaxLength(40); e.Property(x => x.SavedBy).HasMaxLength(120);
            e.Property(x => x.ConfigJson).HasColumnType("nvarchar(max)"); e.Property(x => x.IsCurrent).HasDefaultValue(false);
            e.Property(x => x.CreatedAt).HasDefaultValueSql(now); });
        b.Entity<ConfigKey>(e => { e.ToTable("ConfigKey", "dbo"); e.HasKey(x => x.Key); e.Property(x => x.Key).HasMaxLength(100);
            e.Property(x => x.ValueJson).HasColumnType("nvarchar(max)"); e.Property(x => x.UpdatedAt).HasDefaultValueSql(now); });
        b.Entity<Loan>(e => { e.ToTable("Loan", "dbo"); e.HasKey(x => x.Id);
            e.Property(x => x.AccountId).HasMaxLength(64); e.Property(x => x.Name).HasMaxLength(200); e.Property(x => x.Lender).HasMaxLength(200);
            e.Property(x => x.LoanNumber).HasMaxLength(64); e.Property(x => x.Principal).HasPrecision(18, 2); e.Property(x => x.AprPercent).HasPrecision(9, 4);
            e.Property(x => x.MinPayment).HasPrecision(18, 2); e.Property(x => x.MaturityDate).HasColumnType("date"); e.Property(x => x.StatementDate).HasColumnType("date");
            e.Property(x => x.Source).HasMaxLength(100); e.Property(x => x.Notes).HasMaxLength(1000); e.Property(x => x.UpdatedAt).HasDefaultValueSql(now); });
        b.Entity<Connection>(e => { e.ToTable("Connection", "dbo"); e.HasKey(x => x.Name); e.Property(x => x.Name).HasMaxLength(50);
            e.Property(x => x.Server).HasMaxLength(200); e.Property(x => x.Database).HasMaxLength(128); e.Property(x => x.AuthMode).HasMaxLength(20);
            e.Property(x => x.Username).HasMaxLength(128); e.Property(x => x.SecretLocation).HasMaxLength(400); e.Property(x => x.Notes).HasMaxLength(400);
            e.Property(x => x.UpdatedAt).HasDefaultValueSql(now); });
    }
}

// Design-time only (`dotnet ef ...`). Reads ConnectionStrings:Cfd from the Shell project's
// appsettings.json + appsettings.{DOTNET_ENVIRONMENT, default Development}.json, then env vars
// (ConnectionStrings__Cfd wins). No hardcoded fallback: a missing key fails with a clear message.
public class CfdDbContextFactory : IDesignTimeDbContextFactory<CfdDbContext>
{
    public CfdDbContext CreateDbContext(string[] args)
    {
        var env = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") is { Length: > 0 } e ? e : "Development";
        var config = new ConfigurationBuilder()
            .SetBasePath(FindShellDir())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{env}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();
        return CfdDbContext.Create(CfdConnection.Require(config));
    }

    public static string FindShellDir()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
            {
                var shell = Path.Combine(d.FullName, "AdvancedDirectorCockpitWV3");
                if (File.Exists(Path.Combine(shell, "appsettings.json"))) return shell;
                if (File.Exists(Path.Combine(d.FullName, "appsettings.json")) && File.Exists(Path.Combine(d.FullName, "AdvancedDirectorCockpitWV3.csproj"))) return d.FullName;
            }
        }
        return Directory.GetCurrentDirectory();
    }
}