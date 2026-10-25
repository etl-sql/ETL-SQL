# SAVED VIEW

Creates a private named view of a Portal report's parameters. Portal stores one resolved-state envelope, shared with author bookmarks, and stamps the report revision for drift detection.

## Syntax
```sql
EXECUTE portal BEGIN
  CREATE SAVED VIEW 'ViewName' FOR REPORT 'ReportName'
    [DEFAULT] [PARAMETERS (@name = 'value', ...)] [INTO #view];
  CREATE SAVED VIEW 'ViewName' FOR REPORT 'ReportName'
    [DEFAULT] WITH (PARAMETERS = '@name=value,...') [INTO #view];
  SELECT * FROM eng.saved_views('ReportName');
  DROP SAVED VIEW 'ViewName' FOR REPORT 'ReportName';
END;
```

## Options

- **PARAMETERS** — String assignments to report parameters. Values such as `'001'`, `'2026'` and `'true'` remain strings in the envelope. Use the parenthesized assignments when a value contains a comma or equals sign.
- **DEFAULT** — Makes this the caller's default view for the report, replacing their previous default.
- **INTO #view** — Captures the created view's API response in an engine temporary table.
- **Ownership** — Views belong to the authenticated user and report. Other users cannot resolve or apply them.
- **Revision** — Portal stamps the current script hash. Republishing the report produces a drift warning when an older view is resolved.

## Example

```sql
EXECUTE portal BEGIN
  CREATE SAVED VIEW 'West region' FOR REPORT 'Sales Dashboard'
    PARAMETERS (@region = 'West', @customer_code = '001');
  SELECT * INTO #views FROM eng.saved_views('Sales Dashboard');
END;
SELECT * FROM #views;
```

## API and upgrades

- **Create/update** — Send `stateJson` containing a `ResolvedReportState` JSON envelope. It supports typed scalar parameters, the active page, and named visibility/collapse maps.
- **Default view** — Send the envelope in `state` to the default-view endpoint.
- **Flat fields** — Top-level `parameters` and `filters` are rejected, including explicit nulls. Responses expose `stateJson` and `state`.
- **v0.22.0 upgrade** — Stop older Portal hosts before applying the migration. It preserves existing envelopes and converts old flat string maps once, merging filters over parameters and adding missing `@` prefixes. Strings keep their original contents. Invalid old maps stop the upgrade; repair them or save a valid envelope before retrying. Mixed-version operation across this migration is unsupported.

## References

- [Portal Admin Commands](README.md)
- [Portal Engine Catalog](../eng/portal-catalog.md)
- [Reporting Architecture](../../architecture/reporting.md)
