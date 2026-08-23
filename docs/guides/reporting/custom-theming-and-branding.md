# Custom Theming and Report Branding

Report-SQL provides granular control over visual styling, color palettes, and global dashboard branding. You can customize the look and feel of reports from the browser shell down to individual chart elements using `SET REPORT` directives, named themes, and inline CSS styles.

---

> **Applies to:** every deployment profile (Solo, Team, Enterprise, SaaS).

## Global Shell Settings (`SET REPORT`)

Global settings control the host shell—including the browser tab title, favicon, header banner, custom CSS variables, and navigation mode.

| Directive | Description | Example |
| :--- | :--- | :--- |
| `SET REPORT TITLE` | Dashboard title displayed in browser tab and header | `SET REPORT TITLE = 'Global Operations';` |
| `SET REPORT DESCRIPTION` | Subtitle and catalog search summary | `SET REPORT DESCRIPTION = 'Live fleet tracking';` |
| `SET REPORT THEME` | Default named theme for all unthemed pages | `SET REPORT THEME = 'dark';` |
| `SET REPORT LOGO` | URL or asset path to header logo | `SET REPORT LOGO = '/assets/logo.svg';` |
| `SET REPORT FAVICON` | URL to browser tab icon (.ico, .png, .svg) | `SET REPORT FAVICON = '/assets/fav.png';` |
| `SET REPORT CSS` | Custom CSS injected into the dashboard `<head>` | `SET REPORT CSS = ':root { --primary: #0284c7; }';` |
| `SET REPORT JS` | JavaScript executed on dashboard initialization | `SET REPORT JS = 'console.log("Ready");';` |
| `SET REPORT NAVIGATION`| Override navigation shell mode (`Compact`, `Hidden`) | `SET REPORT NAVIGATION = 'Compact';` |

---

## Styling Hierarchy and Cascade

Styling cascades from global defaults down to individual visual properties:

```
┌────────────────────────────────────────────────────────┐
│ Global Report Defaults (SET REPORT THEME = 'dark')     │
└───────────────────────────┬────────────────────────────┘
                            │ (overridden by)
┌───────────────────────────▼────────────────────────────┐
│ Page Theme Override (CREATE PAGE ... STYLE (THEME=...))│
└───────────────────────────┬────────────────────────────┘
                            │ (overridden by)
┌───────────────────────────▼────────────────────────────┐
│ Visual Style Override (CREATE VISUAL ... STYLE (...))  │
└────────────────────────────────────────────────────────┘
```

---

## Example 1: Corporate Brand Theming with Custom CSS and Logo

Inject custom brand colors and styling rules into the dashboard shell.

```sql
SET REPORT TITLE = 'Acme Corp Executive Metrics';
SET REPORT DESCRIPTION = 'Confidential Financial Review';
SET REPORT THEME = 'light';
SET REPORT LOGO = 'https://assets.example.com/acme-logo.png';
SET REPORT CSS = '
  :root {
    --brand-primary: #0f766e;
    --brand-accent: #f59e0b;
    --card-radius: 12px;
  }
  .report-header { border-bottom: 2px solid var(--brand-primary); }
  .v-card { border-radius: var(--card-radius); box-shadow: 0 4px 6px -1px rgba(0,0,0,0.1); }
';

CREATE CONNECTION db AS MOCKDB();

SELECT Metric, Value INTO #kpis FROM db.Metrics;

CREATE VISUAL RevenueCard AS CARD (
  SOURCE   = #kpis,
  MAPPINGS (VALUE = Value, LABEL = Metric)
);

CREATE PAGE Main AS DASHBOARD (
  LAYOUT (
    STRUCTURE = 'A',
    MAP ('A' = RevenueCard)
  )
);
```

---

## Example 2: Per-Visual Theming and Custom Button Styling

Apply a dark theme to a specific visual and customize button appearance using the `STYLE (...)` block.

```sql
SET REPORT TITLE = 'Dark Themed Analytics Module';

CREATE CONNECTION db AS MOCKDB();

SELECT Category, Revenue INTO #sales FROM db.Sales;

-- Visual with explicit Dark theme override
CREATE VISUAL RevenueBar AS BAR (
  SOURCE   = #sales,
  MAPPINGS (X = Category, Y = Revenue),
  STYLE    (THEME = dark)
);

-- Custom Styled Action Button
CREATE BUTTON RefreshBtn AS (
  TITLE   = 'Refresh Data',
  ACTIONS (ON_CLICK = APPLY_PARAMETERS),
  STYLE   (
    BACKGROUND    = '#2563eb',
    COLOR         = '#ffffff',
    FONT_WEIGHT   = 'BOLD',
    BORDER_RADIUS = '8px',
    PADDING       = '10px 20px',
    BOX_SHADOW    = '0 2px 4px rgba(0,0,0,0.2)'
  )
);

CREATE PAGE Main AS DASHBOARD (
  LAYOUT (
    STRUCTURE = 'A / B',
    MAP (
      'A' = RefreshBtn,
      'B' = RevenueBar
    )
  )
);
```

---

## Common Pitfalls

- **Markdown flags on `SET REPORT`**: Markdown directives like `TITLE_MD` or `SUBTITLE_MD` belong on `CREATE VISUAL` or `CREATE PAGE` blocks. `SET REPORT TITLE` and `SET REPORT DESCRIPTION` accept plain string literals only.
- **Button alias syntax**: Do not use typed aliases like `CREATE BUTTON ... AS PRIMARY_BUTTON`. Use the standard `CREATE BUTTON <name> AS (...)` form and style it with the `STYLE (...)` block.

---

## Related Topics

- [Authoring Dashboards](authoring-dashboards.md) — Core layout and 3-tier logic model.
- [Report Ownership & Freshness Badges](report-badges-and-trust.md) — Visual governance badges.
- [STYLE Reference](../../reference/visuals-reporting/report/style.md) — Style properties and options.
- [THEME Reference](../../reference/visuals-reporting/report/theme.md) — Built-in theme palette definitions.
