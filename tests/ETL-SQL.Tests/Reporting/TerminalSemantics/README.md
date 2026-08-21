# Terminal & Accessibility Semantic Regression Suite

This test directory provides deterministic terminal snapshot infrastructure and regression coverage for ETL-SQL's Grammar of Graphics (GoG) visual rendering across terminal and accessible plain-text viewports.

## Overview

The terminal test harness captures and asserts rendering consistency across terminal viewports of width **40** (narrow / mobile / split-pane), **80** (standard terminal), and **120** (wide console).

### Covered Visuals & Edge Conditions

1. **`BAR` / `HBAR`**:
   - Multi-series partitioned bars
   - Stable category ordering (`AXIS_SORT = ASC`)
   - Negative and zero values (`terminal_bar_negative_zero.rptsql`)
2. **`LINE`**:
   - Braille canvas curve plotting
   - Discontinuous data with `NULL` gaps (`CONNECT_NULLS = OFF`)
3. **`SCATTER`**:
   - Coordinate plane point distribution
4. **`PIE` / `DONUT`**:
   - Proportional slice composition and formatted percentage tables
5. **`TABLE`**:
   - Long labels, deterministic text wrapping, and column truncation
6. **`COMBO`**:
   - Layered series terminal fallbacks
7. **`RULE` Overlays**:
   - `GOAL` benchmark thresholds and `AVERAGE` statistical markers
8. **Accessible Summaries & KPIs**:
   - `CARD` metrics and tabular fallbacks

## Running the Tests

To run the terminal semantics regression tests:

```powershell
dotnet test tests/ETL-SQL.Tests/ETL-SQL.Tests.csproj --filter "FullyQualifiedName~TerminalSemanticSnapshotTests"
```

## Reusable Test Fixture Builders

The `TerminalSemanticFixtureBuilder` allows constructing in-memory `TestChartSpec`, typed `TestChartDataSet`, and `ReportManifest` instances directly in tests without requiring disk I/O or modifying production lowering code:

```csharp
var dataSet = TestChartDataSet.Create(
    new[] { ("Category", typeof(string)), ("Revenue", typeof(double)) },
    new object[] { "Alpha", 150.0 },
    new object[] { "Beta", -50.0 }
);

var spec = new TestChartSpec(
    Name: "CustomBar",
    VisualType: "BAR",
    Title: "Revenue Variance",
    DataSet: dataSet,
    Mappings: new Dictionary<string, string> { ["x"] = "Category", ["y"] = "Revenue" },
    Options: new Dictionary<string, string> { ["AXIS_SORT"] = "ASC" }
);

var manifest = TerminalSemanticFixtureBuilder.BuildReportManifest(spec);
var renderable = TerminalRenderer.RenderPage(manifest.Pages[0], manifest);
var snapshot = TerminalSnapshotHarness.CaptureSnapshot(renderable, 80);
```
