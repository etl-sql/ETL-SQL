# Golden Sales Operations Workflow

This folder is the end-to-end "golden" Report-SQL workflow for ETL-SQL. It is intentionally self-contained so the same script can be used as a README walkthrough, preview target, Portal demo, export target, and regression fixture.

## What It Covers

- Extracts sales rows from `MOCKDB` and regional targets from `data/region_targets.csv`.
- Stages clean analytic rows in `#orders_stage`.
- Preserves rejected rows in `#quality_issues`.
- Exports the staged dataset to `output/orders_stage.csv`, then reads that CSV back into `#orders_exported`.
- Builds cards, charts, validation tables, controls, a reusable control container, pages, and tab navigation.
- Supports interaction through `@Region`, `@MinMargin`, `@ShowIssues`, and optional `@ExportPath`.
- Exposes exportable table visuals for portal CSV export.

## Workflow Map

1. Extract: `MOCKDB` provides operational sales rows and `FLATFILE` provides regional targets.
2. Stage: valid rows are normalized with a reusable `Margin` field.
3. Validate: invalid rows are retained with clear issue labels.
4. Export: the clean stage is written to CSV through a `FLATFILE` connection.
5. Reload: the report reads the generated CSV back into `#orders_exported`.
6. Report: overview, quality, and export pages share the same controls.
7. Publish: the script can be published directly to Portal.
8. Execute: portal execution creates the same manifest used by preview and tests.
9. Interact: controls update report parameters and refresh dependent visuals.
10. Export: the `OrderDetail` table is the stable portal CSV export target.

## Run It

From the repository root:

```powershell
dotnet run --project src\ETL-SQL.ReportPlayer -- samples\golden_workflow\golden_workflow.rptsql
```

For Portal, publish `samples\golden_workflow\golden_workflow.rptsql`, execute the report, change the region or margin controls, then export `OrderDetail` as CSV. Portal or automated runs can pass `@ExportPath` to put the generated staging CSV in a portal-safe temp location.

The regression tests load this exact file, so changes to the sample should preserve the workflow contract unless the tests and docs are updated together.
