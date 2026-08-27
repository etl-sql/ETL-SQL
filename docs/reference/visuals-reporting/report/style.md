# STYLE

Defines reusable visual styling properties or applies inline formatting to visuals, pages, containers, and buttons.

## Syntax

```sql
CREATE STYLE <name> AS (
  PALETTE       = ('<color1>', '<color2>', ...),
  BACKGROUND    = '<color>',
  BORDER        = '<css-border>',
  BORDER_RADIUS = '<css-radius>',
  SHADOW        = ON | OFF | '<css-shadow>',
  COLOR         = '<color>',
  FONT          = '<font-family>',
  FONT_SIZE     = '<css-size>' | n,
  FONT_WEIGHT   = '<css-weight>',
  PADDING       = '<css-padding>',
  OPACITY       = '<css-opacity>',
  WIDTH         = '<css-width>',
  HEIGHT        = '<css-height>',
  THEME         = 'light' | 'dark' | <theme_name>,
  ALLOW_MAXIMIZE = ON | OFF
);
```

Apply a named style with `STYLE = StyleName` on a visual, page, container, or button. Add inline overrides with `STYLE (KEY = value, ...)`. When both clauses exist (`STYLE = Named, STYLE (...)`), the named style is loaded first and inline properties override it.

`ALLOW_MAXIMIZE` is a visual-level viewer option. Data and chart visuals show the maximize button by default. Input and control visuals (`SLICER`, `MULTISELECT`, `DATEPICKER`, `RELDATEPICKER`, `SLIDER`, `SEARCH`, `CHECKBOX`, `TEXTBOX`, `NUMBERBOX`) hide it by default.

## Options

- **PALETTE = ('<c1>', '<c2>', ...)** — Ordered color sequence for categorical series. More-specific palettes (Visual > Container > Page) replace the entire sequence. Palette colors cycle deterministically (`index % palette.Length`). Colors are validated against the effective background (composed across ancestor styles) with a minimum 3.0:1 contrast ratio (WCAG 2.1 Non-text Contrast). Alpha-channel colors (`#RGBA`, `#RRGGBBAA`) are composited over the background before contrast evaluation.
- **COLOR:<series> = '<color>'** — Explicit color override for a specific series identity. Takes precedence over palette sequence colors and maps to `--etl-series-<sanitized>`.
- **BACKGROUND = '<color>'** — Background color fill for visual cards, containers, or buttons (e.g. `'#f8fafc'`, `'transparent'`). Maps to `--etl-surface-card` and `--etl-surface` (and `--etl-bg` on page/report). Also accepts `BACKGROUND_COLOR`.
- **BORDER = '<css-border>'** — Card or container border definition (e.g. `'1px solid #e2e8f0'`, `'none'`). Its color component maps to the color-only `--etl-border` token; authored HTML supplies its own width and style, such as `border: 1px solid var(--etl-border)`.
- **BORDER_RADIUS = '<css-radius>'** — Corner rounding for visual cards, containers, or buttons (e.g. `'8px'`, `'12px'`). Maps to `--etl-radius-md` and `--etl-radius`. Also accepts `BORDER-RADIUS` or `RADIUS`.
- **SHADOW = ON | OFF | '<css-shadow>'** — Card drop shadow. `ON` enables default elevation shadow, `OFF` disables shadow, or specify a custom CSS shadow string (e.g. `'0 4px 6px rgba(0,0,0,0.1)'`). Maps to `--etl-shadow`. Also accepts `BOX_SHADOW`.
- **COLOR = '<color>'** — Primary text color (e.g. `'#0f172a'`). Maps to `--etl-text-primary` and `--etl-text`. Also accepts `TEXT_COLOR` or `FONT_COLOR`.
- **TEXT_MUTED = '<color>'** — Muted / secondary text color (e.g. `'#64748b'`). Maps to `--etl-text-muted` and `--etl-text-secondary`. Also accepts `MUTED_COLOR` or `SUBTITLE_COLOR`.
- **ACCENT = '<color>'** — Accent and brand primary color (e.g. `'#2563eb'`). Maps to `--etl-accent`. Also accepts `ACCENT_COLOR` or `PRIMARY`.
- **SUCCESS = '<color>'** — Status success color (e.g. `'#16a34a'`). Maps to `--etl-success`. Also accepts `SUCCESS_COLOR`.
- **DANGER = '<color>'** — Status danger / error color (e.g. `'#dc2626'`). Maps to `--etl-danger`. Also accepts `DANGER_COLOR`.
- **WARNING = '<color>'** — Status warning color (e.g. `'#eab308'`). Maps to `--etl-warning`. Also accepts `WARNING_COLOR`.
- **INFO = '<color>'** — Status informational color (e.g. `'#0284c7'`). Maps to `--etl-info`. Also accepts `INFO_COLOR`.
- **RADIUS_SM = '<css-radius>'** — Small radius token override (e.g. `'4px'`). Maps to `--etl-radius-sm`.
- **RADIUS_LG = '<css-radius>'** — Large radius token override (e.g. `'12px'`). Maps to `--etl-radius-lg`.
- **FONT = '<font-family>'** — Primary font family (e.g. `'Segoe UI'`, `'Inter, sans-serif'`). Maps to `--etl-font-family`.
- **FONT_MONO = '<font-family>'** — Monospace font family stack. Maps to `--etl-font-mono`.
- **FONT_SIZE = '<css-size>' | n** — Base font size (e.g. `'14px'`, `'0.9rem'`, or numeric point size `13`).
- **FONT_WEIGHT = '<css-weight>'** — Font weight (e.g. `'600'`, `'BOLD'`, `'NORMAL'`).
- **PADDING = '<css-padding>'** — Container or button inner padding (e.g. `'12px'`, `'8px 16px'`).
- **OPACITY = '<css-opacity>'** — Visual card opacity (e.g. `'0.95'`, `'1'`).
- **WIDTH = '<css-width>'** — Fixed visual or container width override (e.g. `'320px'`, `'100%'`).
- **HEIGHT = '<css-height>'** — Fixed visual or container height override (e.g. `'400px'`).
- **THEME = '<theme>'** — Palette theme name (`light`, `dark`, or custom `CREATE THEME` definition).
- **ALLOW_MAXIMIZE = ON | OFF** — Toggles the full-screen visual maximize toolbar button.
- **EXPORT = ON | OFF** — Toggles data export capabilities on visual toolbars.
- **TAG = '<tag_name>'** — Emits a `data-tag="<tag_name>"` attribute on the DOM card for custom CSS selectors via `SET REPORT CSS`.
- **COLLAPSE_MODE = 'DRAWER' | 'INLINE'** — Collapse behavior for collapsible container components.
- **LAYOUT = 'DROPDOWN'** — Render layout mode for slicer and selection controls.

## Series Identity & Design Token Rules

- **Stable Identity Assignment**: Categorical series identities are sorted alphabetically (case-insensitive invariant) to determine their sequence index. Row order or query partition changes do not alter color assignments.
- **Public Series Tokens**: Every resolved series color (both explicit `COLOR:<name>` and palette-assigned) is emitted as `--etl-series-<sanitized>`.
- **Sanitization & Collision Handling**: Series names are lowercased, non-alphanumeric characters are replaced with `-`, leading digits are prefixed with `s-`, and collisions are resolved deterministically with suffixes `-2`, `-3`, etc.

## Examples

```sql
CREATE STYLE BrandTheme AS (
  PALETTE       = ('#2563eb', '#16a34a', '#f59e0b', '#dc2626'),
  BACKGROUND    = '#ffffff',
  BORDER        = '1px solid #e2e8f0',
  BORDER_RADIUS = '8px',
  SHADOW        = ON
);

CREATE VISUAL SalesByRegion AS BAR (
  SOURCE   = #sales,
  MAPPINGS (X = region, Y = revenue),
  TITLE    = 'Regional Sales',
  STYLE    = BrandTheme,
  STYLE    (COLOR:Domestic = '#1d4ed8')
);
```

## References

- [Report-SQL Guide](../../../guides/feature-guides/report-sql.md)
- [Custom Theming and Report Branding](../../../guides/reporting/custom-theming-and-branding.md)
- [THEME Reference](theme.md)
- [BUTTON Reference](button.md)
- [CONTAINER Reference](container.md)
- [Statements](../../statements/README.md)
