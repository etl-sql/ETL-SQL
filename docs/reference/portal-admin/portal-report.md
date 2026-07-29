# Portal Report Management
Publish, update, validate, refresh, and remove reports in the portal inside an `EXECUTE portal` block.

## Syntax
```sql
EXECUTE portal BEGIN
  PUBLISH REPORT 'Sales Dashboard' FROM 'C:\Reports\sales.rptsql' IN FOLDER '/Finance';
  PUBLISH REPORT 'Sales Dashboard' FROM 'C:\Reports\sales.rptsql' IN FOLDER '/Finance'
    WITH (DESCRIPTION = 'Certified sales dashboard');
  ALTER REPORT 'Sales Dashboard' SET DESCRIPTION = 'Q4 certified sales';
  DROP REPORT 'Sales Dashboard';
  REFRESH REPORT 'Sales Dashboard';
  VALIDATE REPORT SCRIPT 'C:\Reports\sales.rptsql' INTO #validation;
  FAVORITE REPORT 'Sales Dashboard';
  UNFAVORITE REPORT 'Sales Dashboard';
END;
```

## Examples
```sql
-- Publish a new report to a folder
EXECUTE portal BEGIN
  PUBLISH REPORT 'Sales Dashboard' FROM 'C:\Reports\sales.rptsql' IN FOLDER '/Finance';
END;

-- Publish with a description
EXECUTE portal BEGIN
  PUBLISH REPORT 'Sales Dashboard' FROM 'C:\Reports\sales.rptsql' IN FOLDER '/Finance'
    WITH (DESCRIPTION = 'Certified sales dashboard');
END;

-- Validate a script for errors before deploying
EXECUTE portal BEGIN
  VALIDATE REPORT SCRIPT 'C:\Reports\sales.rptsql' INTO #validation;
END;
SELECT severity, message, line_number FROM #validation ORDER BY line_number;

-- Move a report to an archive folder and update its description
EXECUTE portal BEGIN
  ALTER REPORT 'Sales Dashboard'
    SET FOLDER = '/Finance/Archive',
        DESCRIPTION = 'Q4 certified sales';
END;

-- Trigger an immediate data refresh on a deployed report
EXECUTE portal BEGIN
  REFRESH REPORT 'Sales Dashboard';
END;

-- Mark a report as a personal favorite
EXECUTE portal BEGIN
  FAVORITE REPORT 'Sales Dashboard';
END;

-- Remove a report from the portal
EXECUTE portal BEGIN
  DROP REPORT 'Q4 Sales';
END;
```

## Notes
- `PUBLISH REPORT` compiles and deploys a `.rptsql` file to the portal. The file path must be accessible from the machine running the ETL-SQL engine, not the portal server.
- The report catalog name is the quoted identity immediately after `PUBLISH REPORT`.
- `WITH (DESCRIPTION = '...')` stores portal-facing report metadata during publish.
- `ALTER REPORT` can update `FOLDER` and `DESCRIPTION` for an existing report.
- `VALIDATE REPORT SCRIPT` checks the script for parse errors and lint warnings without deploying. The `INTO` clause is required and captures diagnostic rows with columns: `severity`, `code`, `message`, and `line_number`.
- `REFRESH REPORT` triggers an immediate data refresh cycle for all datasets declared in the report, bypassing the scheduled refresh interval.
- `FAVORITE` and `UNFAVORITE` apply to the currently authenticated portal user's personal favorites list.
- Dropping a report does not remove saved views, alerts, or subscriptions linked to it — clean those up first using `DROP SAVED VIEW`, `DROP ALERT`, and `DROP SUBSCRIPTION`.
- See: PORTAL_SAVEDVIEW, PORTAL_ALERT, PORTAL_SUBSCRIPTION, PORTAL_DATASET, PORTAL_SHOW

References:
- [Data Connectors](../../administration/platform/README.md)
- [Portal Admin Commands](README.md)
