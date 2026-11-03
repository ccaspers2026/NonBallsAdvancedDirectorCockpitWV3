using AdvancedDirectorCockpitWV3.Data.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AdvancedDirectorCockpitWV3.Data.Repositories;

public sealed record CfdProbe(string Database, string Server, int LoanCount);

// Read-only probe: same query WV3 used at startup before the reorg.
public sealed class CfdProbeRepository
{
    private readonly string _cs;
    public CfdProbeRepository(string connectionString) { ArgumentException.ThrowIfNullOrWhiteSpace(connectionString); _cs = connectionString; }

    public CfdProbe Probe()
    {
        using var conn = new SqlConnection(_cs);
        conn.Open();
        using var cmd = new SqlCommand("SELECT DB_NAME(), @@SERVERNAME, (SELECT COUNT(*) FROM dbo.Loan)", conn);
        using var r = cmd.ExecuteReader();
        r.Read();
        return new CfdProbe(r.GetString(0), r.GetString(1), r.GetInt32(2));
    }

    // Number of user tables in the database (0 = brand-new empty CFD).
    public int UserTableCount()
    {
        using var conn = new SqlConnection(_cs);
        conn.Open();
        using var cmd = new SqlCommand("SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped = 0", conn);
        return (int)cmd.ExecuteScalar()!;
    }
}

public sealed class LoanRepository
{
    private readonly string _cs;
    public LoanRepository(string connectionString) { ArgumentException.ThrowIfNullOrWhiteSpace(connectionString); _cs = connectionString; }
    public List<Loan> GetAll() { using var db = CfdDbContext.Create(_cs); return db.Loan.AsNoTracking().OrderBy(l => l.Id).ToList(); }
}

public sealed class ConfigKeyRepository
{
    private readonly string _cs;
    public ConfigKeyRepository(string connectionString) { ArgumentException.ThrowIfNullOrWhiteSpace(connectionString); _cs = connectionString; }
    public List<ConfigKey> GetAll() { using var db = CfdDbContext.Create(_cs); return db.ConfigKey.AsNoTracking().OrderBy(k => k.Key).ToList(); }
}