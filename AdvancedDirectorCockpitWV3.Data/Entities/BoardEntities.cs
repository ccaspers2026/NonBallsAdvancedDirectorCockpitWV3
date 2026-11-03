namespace AdvancedDirectorCockpitWV3.Data.Entities;

// 2026-11-02 board in the database (was data\cards.json, groups.json, tabs.json, stages.json + data\icons).
// Each row keeps the page's full JSON object (Json) so every field round-trips unchanged; key fields are
// promoted to columns for lookups. Seq = position in the array the page last saved.
public class BoardCard
{
    public string Id { get; set; } = ""; public int Seq { get; set; } public string? GroupId { get; set; } public string? Name { get; set; }
    public string? App { get; set; } public string? Url { get; set; } public string Json { get; set; } = ""; public DateTimeOffset UpdatedAt { get; set; }
}

public class BoardGroup
{
    public string Id { get; set; } = ""; public int Seq { get; set; } public string? ParentId { get; set; } public string? Title { get; set; }
    public string Json { get; set; } = ""; public DateTimeOffset UpdatedAt { get; set; }
}

public class BoardTab
{
    public string Id { get; set; } = ""; public int Seq { get; set; } public string? Label { get; set; }
    public string Json { get; set; } = ""; public DateTimeOffset UpdatedAt { get; set; }
}

public class BoardStageLayout
{
    public string Id { get; set; } = ""; public int Seq { get; set; } public string Json { get; set; } = ""; public DateTimeOffset UpdatedAt { get; set; }
}

// Non-array document keys: doc.<cards|groups|tabs|stages>.<key> (e.g. doc.stages.nativeSlot, doc.stages.defaults, doc.cards._readme).
public class BoardSetting
{
    public string Key { get; set; } = ""; public int Seq { get; set; } public string? ValueJson { get; set; } public DateTimeOffset UpdatedAt { get; set; }
}

// Card images served as data/icons/<Name> (site favicons <host-slug>.png, uploads upload_<name>.png).
public class BoardIcon
{
    public string Name { get; set; } = ""; public byte[] Content { get; set; } = []; public DateTimeOffset UpdatedAt { get; set; }
}
