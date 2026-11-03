-- dbUp seed 0001 (idempotent). Only runs on a brand-new empty CFD (see CfdStartupService).
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMeta)
    INSERT INTO dbo.SchemaMeta (Version, Notes) VALUES (1, N'Seeded by WV3 dbUp (empty database)');