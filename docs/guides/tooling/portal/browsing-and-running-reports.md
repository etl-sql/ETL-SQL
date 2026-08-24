# Browsing and Running Reports

> **Applies to:** Solo · Team · Enterprise · SaaS

The ETL-SQL Portal is a web application for discovering, running, and interacting with published Report-SQL dashboards. This guide covers catalog navigation, search, report execution, interactive parameters, and freshness indicators.

---

## Navigating the Catalog

When you sign in the **Reports** page opens showing a folder tree in the left sidebar.

- Click a folder to expand its sub-folders and reports.
- Folders you have no access to are not shown.
- A **stale** badge next to a report name means the script has been modified since the last snapshot was built. An admin or authorized user can click **Refresh** to rebuild it.

Use the **search bar** at the top to find reports by keyword, tag, or folder path. Results show the report name, folder, last-refreshed timestamp, and owner badge.

> [!TIP]
> Pin frequently-used reports to **Favorites** (star icon) so they appear in the **Favorites** section at the top of your sidebar.

---

## Opening a Report

Click any report name to open it. The Portal loads the most recent **snapshot** — a pre-built result set stored server-side.

| State | What you see |
| :--- | :--- |
| Snapshot available | Dashboard visuals render immediately |
| No snapshot yet | A **Run** button appears in the report panel |
| Snapshot stale | Dashboard renders but a stale badge warns data may be outdated |

---

## Running and Refreshing a Report

Click **Run** (or **Refresh**) to re-execute the underlying `.rptsql` script against live data sources. Execution is asynchronous — a progress indicator shows while the job runs.

> [!IMPORTANT]
> Running a report re-executes the script against live connections. On large datasets this may take several minutes. Administrators can set per-report execution timeouts.

---

## Interactive Parameters

Reports authored with interactive controls expose a filter panel above the dashboard. Common control types:

| Control | What it does |
| :--- | :--- |
| **SLICER** | Click a value to filter all visuals on the page |
| **DATEPICKER** | Pick a date to pass as a script parameter |
| **MULTISELECT** | Choose multiple values from a dropdown list |
| **SLIDER** | Drag to select a numeric threshold |
| **SEARCH** | Type to filter a visual's displayed rows |

After changing a control value the report reruns with your new inputs. Some reports apply filters instantly (client-side); others execute a new server run.

### Example: Slicer binding in Report-SQL

A report author declares this to drive the filter panel you interact with:

```sql
DECLARE @region VARCHAR(100) = 'All';

SELECT DISTINCT Region AS Value INTO #regions FROM prod.dbo.Orders;

CREATE VISUAL RegionSlicer AS SLICER (
    SOURCE   = #regions,
    MAPPINGS (VALUE = Value),
    ACTIONS  (ON_CHANGE = SET_PARAMETER(@region, Value))
);

CREATE VISUAL SalesChart AS BAR (
    SOURCE   = (
        SELECT Region, SUM(Revenue) AS Revenue
        FROM prod.dbo.Orders
        WHERE @region = 'All' OR Region = @region
        GROUP BY Region
    ),
    MAPPINGS (X = Region, Y = Revenue)
);
```

### Example: Date range with two date pickers

```sql
DECLARE @startDate DATE = 'D-30';
DECLARE @endDate   DATE = 'D-0';

CREATE VISUAL StartPicker AS DATEPICKER (
    ACTIONS (ON_CHANGE = SET_PARAMETER(@startDate, value))
);

CREATE VISUAL EndPicker AS DATEPICKER (
    ACTIONS (ON_CHANGE = SET_PARAMETER(@endDate, value))
);
```

---

## Freshness Badges

Each report card and report header displays a **freshness badge** indicating when data was last refreshed:

| Badge | Meaning |
| :--- | :--- |
| **Live** | Data was just refreshed (under 5 minutes ago) |
| **1h ago** / **3h ago** | Approximate age of the current snapshot |
| **Stale** | The source script changed since the last snapshot |
| **Scheduled** | A refresh job is configured and will run automatically |

---

## Related Guides

- [Exporting and Subscriptions](exporting-and-subscriptions.md) — download snapshots and set up email delivery
- [Saved Views and Bookmarks](saved-views-and-bookmarks.md) — save your parameter presets
- [Report-SQL Guide](../../feature-guides/report-sql.md) — full `.rptsql` authoring reference
