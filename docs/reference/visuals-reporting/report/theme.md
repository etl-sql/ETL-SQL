# CREATE THEME

Defines a renderer-neutral color theme that can be applied to any visual or page with `STYLE (THEME = themeName)`. Themes are saved as JSON files to `{TemplatePath}/Themes/` and embedded in the report manifest so every supported report surface can resolve the same tokens.

```sql
CREATE THEME corporate AS (
  BACKGROUND   = '#1a1a2e',       -- chart / card background
  TEXT_COLOR   = '#eeeeee',       -- title, legend, axis labels
  ACCENT_COLOR = '#4ecca3',       -- primary series color
  COLORS       = '#4ecca3, #e94560, #f5a623, #0078d4',  -- full palette
  GRID_COLOR   = '#2a2a4e'        -- axis grid lines
);
```

Apply the theme just like any built-in theme:

```sql
CREATE VISUAL RevenueChart AS BAR (

```sql
CREATE VISUAL RevenueChart AS BAR (
  SOURCE   = (SELECT Month, Revenue FROM #data),
  MAPPINGS (X = Month, Y = Revenue),
  STYLE    (THEME = corporate)
);
```

### Supported theme properties

| Property | Description |
|---|---|
| `BACKGROUND` | Chart and card background fill. |
| `TEXT_COLOR` | Default title, legend, axis-label, and narrative text color. |
| `ACCENT_COLOR` | First or primary series color when `COLORS` is omitted. |
| `COLORS` | Comma-separated palette or parenthesized list used for ordered series. |
| `FONT_FAMILY` | Typography font stack (e.g. `'Inter, sans-serif'`) applied across text styles. |
| `AXIS_COLOR` | Axis line, tick, and label color; inherits `TEXT_COLOR` when omitted. |
| `GRID_COLOR` | Axis grid-line color. |
| `[VISUAL] (...)` | Per-visual-type overrides (e.g., `[BAR] (COLORS = ('#e11d48', '#f59e0b'))`). |
| Any other key | Retained as a lower-case extension token for report surfaces that explicitly support it. |

### Per-visual theme overrides

You can declare visual-specific color overrides within a theme:

```sql
CREATE THEME BrandTheme AS (
  PRIMARY     = '#1e40af',
  FONT_FAMILY = 'Inter, sans-serif',
  [BAR]  (COLORS = ('#e11d48', '#f59e0b')),
  [LINE] (COLORS = ('#2563eb', '#10b981'))
);
```

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

