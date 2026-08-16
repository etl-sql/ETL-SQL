# CREATE THEME

Defines a custom ECharts color theme that can be applied to any visual or page with `STYLE (THEME = themeName)`. Themes are saved as JSON files to `{TemplatePath}/Themes/` and embedded in the report manifest so the web player can register them at render time.

```sql
CREATE THEME corporate AS (
  BACKGROUND   = '#1a1a2e',       -- chart / card background
  TEXT_COLOR   = '#eeeeee',       -- title, legend, axis labels
  ACCENT_COLOR = '#4ecca3',       -- primary series color
  COLORS       = '#4ecca3, #e94560, #f5a623, #0078d4',  -- full palette
  GRID_COLOR   = '#2a2a4e'        -- axis grid lines
);
```

Apply the theme just like any built-in ECharts theme:

```sql
CREATE VISUAL RevenueChart AS BAR (
  SOURCE   = (SELECT Month, Revenue FROM #data),
  MAPPINGS (X = Month, Y = Revenue),
  STYLE    (THEME = corporate)
);
```

### Supported theme properties

| Property | Maps to ECharts | Description |
|---|---|---|
| `BACKGROUND` | `backgroundColor` | Chart background fill |
| `TEXT_COLOR` | `textStyle.color`, title, legend, axis label colors | Default text color everywhere |
| `ACCENT_COLOR` | `color[0]` | First (primary) series color |
| `COLORS` | `color` array | Comma-separated hex list for all series |
| `AXIS_COLOR` | Axis line, tick, and label colors | If omitted, inherits `TEXT_COLOR` |
| `GRID_COLOR` | `splitLine.lineStyle.color` | Axis grid line color |
| Any other key | Passed through as-is to root | Use for ECharts-specific overrides |

### DROP THEME

```sql
DROP THEME corporate;
DROP THEME IF EXISTS corporate;
```

Removes the theme from memory and deletes the `.json` file from disk.

### Theme lifecycle

`CREATE`, `CREATE OR REPLACE`, and `DROP` — there is deliberately no `ALTER THEME`. A theme is a
small property bag written out as one JSON file, so redefining it is both shorter than a patch and
leaves the file on disk unambiguous:

```sql
CREATE OR REPLACE THEME corporate AS (
  BACKGROUND = '#101018',
  TEXT_COLOR = '#eeeeee'
);
```

`ALTER THEME` is rejected by the parser, before any statement in the script runs, with a message
naming this form.

## References

- [Report-SQL Reference](../README.md)
- [Syntax Index](../../../syntax-index.md)

