# Themed Dashboard with CREATE STYLE

**Pattern**: Define a shared visual identity once with `CREATE STYLE`, then apply it across all visuals, pages, and containers. Override individual properties inline where needed.

**Demonstrates**: `CREATE STYLE`, `STYLE = <name>`, inline `STYLE (...)` overrides, applying styles to visuals / pages / containers.

```sql
SET REPORT TITLE       = 'Themed Sales Overview';
SET REPORT DESCRIPTION = 'Demonstrates CREATE STYLE for consistent branding across all report objects.';

-- ── Sample data ───────────────────────────────────────────────────────────
SELECT 'Jan' AS month, 142000 AS revenue, 88 AS orders INTO #summary
UNION ALL SELECT 'Feb', 158000, 97
UNION ALL SELECT 'Mar', 173000, 110
UNION ALL SELECT 'Apr', 191000, 124
UNION ALL SELECT 'May', 167000, 103
UNION ALL SELECT 'Jun', 205000, 138;

SELECT 'North' AS region, 312000 AS revenue INTO #byregion
UNION ALL SELECT 'South', 248000
UNION ALL SELECT 'East',  197000
UNION ALL SELECT 'West',  279000;

-- ── Named styles ─────────────────────────────────────────────────────────
-- Base dark theme applied to most visuals
CREATE STYLE DarkTheme AS (
  background-color = '#1e1e2e',
  color            = '#cdd6f4',
  border-radius    = '8px',
  padding          = '12px',
  font-size        = '13px'
);

-- Accent for KPI cards that need emphasis
CREATE STYLE KpiAccent AS (
  background-color = '#313244',
  color            = '#89dceb',
  border           = '1px solid #45475a',
  border-radius    = '10px',
  padding          = '16px',
  font-size        = '15px'
);

-- Subtle border for layout containers
CREATE STYLE PanelFrame AS (
  border        = '1px solid #45475a',
  border-radius = '6px',
  padding       = '8px'
);

-- ── Visuals ───────────────────────────────────────────────────────────────
-- KPI card — uses KpiAccent style
CREATE VISUAL TotalRevenue AS CARD (
  TITLE    = 'Total Revenue',
  SOURCE   = (SELECT SUM(revenue) AS revenue, 'Total' AS label FROM #summary),
  STYLE    = KpiAccent,
  MAPPINGS (VALUE = revenue, LABEL = label),
  OPTIONS  (FORMAT = 'C0')
);

-- KPI card — uses KpiAccent but overrides color to signal a warning
CREATE VISUAL TotalOrders AS CARD (
  TITLE    = 'Total Orders',
  SOURCE   = (SELECT SUM(orders) AS orders, 'Orders' AS label FROM #summary),
  STYLE    = KpiAccent,
  STYLE    (color = '#a6e3a1'),   -- green override
  MAPPINGS (VALUE = orders, LABEL = label)
);

-- Trend line — base DarkTheme
CREATE VISUAL RevenueTrend AS LINE (
  TITLE    = 'Revenue by Month',
  SOURCE   = #summary,
  STYLE    = DarkTheme,
  MAPPINGS (X = month, Y = revenue)
);

-- Regional bar chart — base DarkTheme, wider padding for readability
CREATE VISUAL RegionBar AS BAR (
  TITLE    = 'Revenue by Region',
  SOURCE   = #byregion,
  STYLE    = DarkTheme,
  STYLE    (padding = '20px'),    -- inline override
  MAPPINGS (X = region, Y = revenue)
);

-- Detail table — DarkTheme with alternating row color override
CREATE VISUAL SummaryTable AS TABLE (
  TITLE    = 'Monthly Detail',
  SOURCE   = #summary,
  STYLE    = DarkTheme,
  STYLE    (font-size = '12px')
);

-- ── Container ─────────────────────────────────────────────────────────────
-- Wrap KPI cards in a styled horizontal container
CREATE CONTAINER KpiRow AS BOX (
  STYLE   = PanelFrame,
  LAYOUT (
    STRUCTURE = 'A B',
    MAP (
      'A' = TotalRevenue,
      'B' = TotalOrders
    )
  )
);

-- ── Page layout ───────────────────────────────────────────────────────────
CREATE PAGE Overview AS DASHBOARD (
  TITLE     = 'Sales Overview',
  STRUCTURE = 'A A / B C / D D',
  STYLE     = PanelFrame,
  MAP ('A' = KpiRow,
       'B' = RegionBar,
       'C' = RevenueTrend,
       'D' = SummaryTable)
);

CREATE NAVIGATION MainNav AS TAB (
  ORIENTATION = HORIZONTAL,
  DEFAULT     = Overview,
  PAGES (Overview)
);
```

### Key Points

- **`CREATE STYLE <name> AS (...)`** defines a reusable style block. Declare styles before the visuals that reference them.
- **`STYLE = <name>`** applies the named style as a base. All properties from the named style are inherited.
- **`STYLE (...)`** on the same visual overrides specific properties; other properties from the named style are unchanged.
- Both `STYLE = <name>` and an inline `STYLE (...)` block can appear on the same visual — the named style is the base and the inline block wins on any overlapping key.
- Styles apply to `CREATE VISUAL`, `CREATE PAGE`, and `CREATE CONTAINER` equally.
- Named styles are resolved at manifest build time and are not stored as separate entities in the manifest output.

**Conditional formatting is ordered** — rules evaluate top-to-bottom, first match wins. Put the strictest condition first.
