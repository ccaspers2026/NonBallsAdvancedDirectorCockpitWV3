namespace AdvancedDirectorCockpitWV3.Data.Entities;

public class SchemaMeta { public int Version { get; set; } public DateTimeOffset AppliedAt { get; set; } public string? Notes { get; set; } }

public class ConfigDocument
{
    public int Id { get; set; } public int Rev { get; set; } public string? SavedAt { get; set; } public string? SavedBy { get; set; }
    public string ConfigJson { get; set; } = ""; public bool IsCurrent { get; set; } public DateTimeOffset CreatedAt { get; set; }
}

public class ConfigKey { public string Key { get; set; } = ""; public string? ValueJson { get; set; } public int Rev { get; set; } public DateTimeOffset UpdatedAt { get; set; } }

public class Loan
{
    public int Id { get; set; } public string? AccountId { get; set; } public string Name { get; set; } = ""; public string? Lender { get; set; }
    public string? LoanNumber { get; set; } public decimal? Principal { get; set; } public decimal? AprPercent { get; set; } public decimal? MinPayment { get; set; }
    public DateOnly? MaturityDate { get; set; } public string? Source { get; set; } public DateOnly? StatementDate { get; set; } public string? Notes { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class Connection
{
    public string Name { get; set; } = ""; public string Server { get; set; } = ""; public string Database { get; set; } = ""; public string AuthMode { get; set; } = "";
    public string? Username { get; set; } public string? SecretLocation { get; set; } public string? Notes { get; set; } public DateTimeOffset UpdatedAt { get; set; }
}