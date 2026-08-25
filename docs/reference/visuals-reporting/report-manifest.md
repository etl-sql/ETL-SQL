# ReportManifest JSON Schema

The compiled `ReportManifest` is the structure returned by the snapshot and by the ReportPlayer API
(`/api/manifest`). It captures the report's visuals, pages, containers, navigations, and datasets. For
the runtime rendering contract, see [Report runtime contract](report-runtime-contract.md).

```jsonc
{
  "source":      "C:/reports/sales.rptsql",
  "builtAt":     "2026-04-12T18:00:00Z",
  "title":       "Sales Dashboard",
  "description": "Regional revenue analysis",

  // Server-resolved formatting. Renderers read this instead of the viewer's machine.
  "formatting": {
    "locale":    "",
    "timeZone":  "UTC",
    "nullLabel": "-"
  },

  "visuals": [
    {
      "name":       "RevenueByRegion",
      "visualType": "Bar",
      "chartSpec":   { "schema": "https://etl-sql.org/schemas/reporting/chart-spec/v1", "version": 1, "id": "RevenueByRegion" },
      "chartData":   { "schema": "https://etl-sql.org/schemas/reporting/chart-data/v1", "version": 1, "name": "inline:RevenueByRegion", "rowCount": 2 },
      "plotPlan":    { "schema": "https://etl-sql.org/schemas/reporting/plot-plan/v1", "version": 1, "specId": "RevenueByRegion" },
      "nativeSvg":   "<svg xmlns='http://www.w3.org/2000/svg' ...>...</svg>",
      "columns":  ["region", "revenue"],
      "rows":     [["East", "12000"], ...],
      "options":  {
        "mapping:x":       "region",
        "mapping:y":       "revenue",
        "axis:x:label":    "Region",
        "axis:y:label":    "Revenue ($)"
      },
      "styles":   { "THEME": "dark" },
      "actions":  [
        { "type": "SET_PARAMETER", "trigger": "ON_CHANGE",
          "parameterName": "@region", "valueExpression": "region" }
      ]
    }
  ],

  "pages": [
    {
      "name":      "Overview",
      "structure": "A B / C C",
      "slotMap":   { "A": "TotalRevenue", "B": "RegionFilter", "C": "RevenueByRegion" },
      "styles":    { "THEME": "dark" }
    }
  ],

  "containers": [
    {
      "name":          "KpiRow",
      "containerType": "BOX",
      "structure":     "A B",
      "slotMap":       { "A": "TotalRevenue", "B": "TotalUnits" },
      "styles":        { "HEIGHT": "200" }
    }
  ],

  "navigations": [
    {
      "name":        "MainNav",
      "navType":     "TAB",
      "orientation": "HORIZONTAL",
      "defaultPage": "Overview",
      "pages":       ["Overview", "Details"]
    }
  ],

  "datasets": [
    {
      "tempTableName":   "&sales_snap",
      "refreshInterval": "1h",
      "ttl":             "24h",
      "lastRefresh":     "2026-04-12T18:00:00Z",
      "rowCount":        4800
    }
  ]
}
```

## Formatting

`formatting` is always present and always resolved on the server, so the browser, PDF, email, and
terminal renderings of one report agree.

- **locale** — culture name used for dates, times, and computed numbers; the empty string is the invariant culture.
- **timeZone** — zone every instant in the report is rendered in.
- **nullLabel** — text rendered in place of a NULL value.

A visual whose `OPTIONS` carry `NULL_LABEL` overrides the report-level label for that visual only.
See [SET REPORT](../set-commands/set-report.md) for the full precedence chain.

## References

- [SET REPORT](../set-commands/set-report.md)
- [Report runtime contract](report-runtime-contract.md)
- [Report CLI, Hosting, and Preview](report-cli.md)
- [Report objects](report/README.md)
