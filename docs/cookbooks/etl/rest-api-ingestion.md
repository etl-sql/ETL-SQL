# REST API Ingestion
Pull data from a REST API and load it into a database table. The `API` connector auto-handles authentication, pagination, and JSON path extraction.

**Pattern Scenario:** Sync GitHub issues from a public repository API into a tracking database.

```sql
CREATE CONNECTION github AS API(
        URL       = 'https://api.github.com/repos/myorg/myrepo/issues',
        AUTH_TYPE = 'Bearer',
        TOKEN     = 'ENC:U2FsdGVk...',    -- GitHub Personal Access Token (encrypted)
        ROOT_PATH = '$',                   -- the response IS the array
        PAGINATION_MODE = 'PAGE',         -- GitHub uses page pagination
        PAGE_SIZE       = 100
    );

CREATE CONNECTION dest AS MSSQL(SERVER='tracker-db', DATABASE='Issues', TRUSTED_CONNECTION=TRUE);

BEGIN TRY
    -- 1. Pull all open issues from the API (pagination handled automatically)
    SELECT
        id          AS IssueId,
        number      AS IssueNumber,
        title       AS Title,
        state       AS State,
        created_at  AS CreatedAt,
        updated_at  AS UpdatedAt
    INTO #issues
    FROM github;

    PRINT 'Retrieved ' + CAST((SELECT COUNT(*) FROM #issues) AS STRING) + ' issues from API.';

    -- 2. Upsert into the tracking table
    MERGE INTO dest.dbo.GitHubIssues AS T
    USING #issues AS S ON T.IssueId = S.IssueId
    WHEN MATCHED AND S.UpdatedAt > T.UpdatedAt THEN
        UPDATE SET T.Title = S.Title, T.State = S.State, T.UpdatedAt = S.UpdatedAt
    WHEN NOT MATCHED THEN
        INSERT (IssueId, IssueNumber, Title, State, CreatedAt, UpdatedAt)
        VALUES (S.IssueId, S.IssueNumber, S.Title, S.State, S.CreatedAt, S.UpdatedAt);

    PRINT 'Issue sync complete.';
END TRY
BEGIN CATCH
    PRINT 'API ingestion failed: ' + ERROR_MESSAGE();
    THROW;
END CATCH;
```
