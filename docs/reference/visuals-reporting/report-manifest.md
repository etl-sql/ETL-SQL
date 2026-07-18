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

  "visuals": [
    {
      "name":       "RevenueByRegion",
      "visualType": "Bar",
      "chartConfig": "{ /* ECharts option object JSON */ }",
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

## References

- [Report runtime contract](report-runtime-contract.md)
- [Report CLI, Hosting, and Preview](report-cli.md)
- [Report objects](report/README.md)
