# Seeds (dbUp, embedded)
Run after EF Migrate on every start (journal dbo.SchemaVersions_Seed; each script once per database).
- 0001_SchemaMeta.sql: SchemaMeta row (only when empty).
- 0002_BoardSeed.sql: board (BoardGroup, BoardCard, BoardTab, BoardStageLayout, BoardSetting). Generated 2026-11-02 from the last canonical data\cards.json, groups.json, tabs.json, stages.json, deduplicated by id (first wins). Each table is filled only when empty.
- 0003_BoardIcons.sql: BoardIcon rows from the old data\icons folder (served as data/icons/<name>). IF NOT EXISTS per icon.
Never edit an applied script; add a new numbered one.