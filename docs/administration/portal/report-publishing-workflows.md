# Report Publishing Workflows

> **Applies to:** Team · Enterprise · SaaS

Publish a `.rptsql` script as a named, versioned report in a Portal folder — via CLI scripting, the web GUI, or environment promotion.

> [!TIP]
> See [Publishing Reports](publishing.md) for the overview hub, or [Report Versioning and Promotion](report-versioning-and-promotion.md) for dataset key rotation and upgrade procedures.

---

## Publishing via the Portal GUI

1. Upload or copy the `.rptsql` file into the Portal's `ScriptRootPath` directory.
2. Open **Admin → Folders**, select the destination folder.
3. Click **Publish Report** and fill in:
   - **Name** — the display name shown in the Portal catalog
   - **Description** — optional summary
   - **Script path** — path to the `.rptsql` file, relative to `ScriptRootPath`

The Portal validates that the path stays within `ScriptRootPath`. Path traversal attacks are blocked.

---

## Publishing via Script (Script-First CLI)

Use `PUBLISH REPORT` inside an `EXECUTE portal BEGIN...END` block for repeatable, source-controlled publishing:

```sql
EXECUTE portal BEGIN
  PUBLISH REPORT 'Monthly Sales'
      FROM 'reports/finance/monthly_sales.rptsql'
      IN FOLDER '/Finance'
      WITH (
          DESCRIPTION = 'Monthly revenue by region',
          TAGS = 'finance,monthly,certified'
      );
END;
```

---

## Report Metadata Tags

The Portal reads metadata from script header comments. These tags populate catalog fields automatically on publish:

| Tag | Portal field |
| :--- | :--- |
| `@owner` | Report owner/team |
| `@contact` | Support contact |
| `@tags` | Search/category tags, comma-separated |
| `@category` | Primary catalog category |
| `@domain` | Business/data domain |
| `@steward` | Data/report steward |
| `@certification` or `@trusted` | Trust/certification marker |
| `@description` or `@d` | Report description when no publish description is supplied |

```sql
/* @owner: Finance BI
   @contact: finance-bi@example.com
   @tags: revenue,monthly,kpi
   @category: Finance
   @certification: trusted */
SET REPORT TITLE = 'Monthly Sales';
```

Publish request fields override script tags when both are supplied. On republish with a new script path, the Portal refreshes stored metadata from the new script while preserving explicit request values.

> [!NOTE]
> When the active signed organization policy declares `metadata.requiredTags`, publishing also checks report metadata and the column lineage of every `CREATE DATASET` in the script. Missing required tags return `400 organization_metadata_policy`. See [Authoritative Organization Policy](../platform/organization-policy.md).

---

## Validating Before Publishing

Before publishing or replacing a report script, the Portal validates that the file exists under `ScriptRootPath`, has a `.rptsql` extension, and parses successfully. Use `VALIDATE REPORT SCRIPT` to run the same validation:

```sql
VALIDATE REPORT SCRIPT 'reports/finance/monthly_sales.rptsql' INTO #validation;
SELECT * FROM #validation;
```

Or via REST:
```text
POST /api/reports/validate
Content-Type: application/json

{ "scriptPath": "reports/finance/monthly_sales.rptsql" }
```

The response includes the script hash, last modified time, script metadata tags, input parameters, and parse errors when validation fails.

---

## Script Hash Pinning

When a report is published, the Portal computes a SHA-256 hash of the `.rptsql` file and stores it as `PublishedScriptHash`. At every execution, a fresh hash is computed and recorded as `ScriptHashAtRunTime` alongside a `HashMatched` flag.

> [!NOTE]
> The hash is advisory — execution is not blocked by a mismatch. Use `scriptChanged = true` (returned in `GET /api/reports/{id}`) as a signal to re-publish after intentional changes, or to investigate unexpected modifications.

Inspect lifecycle metadata:

```sql
SELECT * INTO #report_history FROM eng.report_history('Monthly Sales');
```

---

## Updating a Report

Edit the `.rptsql` file on disk. The Portal detects the modification timestamp and marks the report as **stale** until a new snapshot is built (snapshots are not rebuilt automatically; a user with Execute permission or an Orchestrator dataset job must trigger a refresh). If you intentionally changed the script, re-publish to reset the pinned hash.

## Deleting a Report

Soft-delete via the report's **Delete** button. The record is marked `IsDeleted = true` and hidden from users; snapshots are retained on disk. Hard deletion requires removing the database record and snapshot files manually.

---

## Environment Promotion Pattern

Use ETL-SQL environment sets as the deployment boundary — do not create a separate portal deployment language for dev/test/prod.

```sql
CREATE SETS !DEV
BEGIN
    @PortalEnvironment = 'DEV'
END

CREATE SETS !PROD
BEGIN
    @PortalEnvironment = 'PROD';
    SET WITH_PROMPT ON;
END

USE SETS !PROD;

IF @PortalEnvironment = 'PROD'
BEGIN
    CREATE FOLDER '/Finance';

    PUBLISH REPORT 'Monthly Sales'
        FROM 'reports/prod/monthly_sales.rptsql'
        IN FOLDER '/Finance'
        WITH (
            DESCRIPTION = 'Monthly revenue by region',
            TAGS = 'finance,monthly,certified'
        );

    GRANT EXECUTE ON FOLDER '/Finance' TO GROUP 'FinanceAnalysts';
END

EXECUTE orch BEGIN
    CREATE SCHEDULE FinanceMorning ON '0 6 * * *';
    CREATE JOB MonthlySalesRefresh FOR REPORT '/Finance/Monthly Sales';
    ALTER JOB MonthlySalesRefresh ADD SCHEDULE FinanceMorning;
END;
```

Promotion is a normal script replay with a different active set and explicit Portal literals for the target environment. Follow with `REFRESH REPORT` after publish succeeds. Keep promotion scripts in source control next to the report scripts they publish.

---

## Related

- [Publishing Reports](publishing.md) — overview hub
- [Report Versioning and Promotion](report-versioning-and-promotion.md) — dataset key rotation and upgrade procedures
- [Embed Tokens and Sharing](embed-tokens-and-sharing.md) — share links, embed tokens, saved views, alerts
- [Portal Administration](README.md)
