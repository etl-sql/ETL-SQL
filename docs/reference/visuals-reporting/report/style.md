# STYLE

Defines reusable visual styling properties or applies inline formatting to visuals, pages, containers, and buttons.

## Syntax

```sql
CREATE STYLE <name> AS (
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

Apply a named style with `STYLE = StyleName` on a visual, page, container, or button. Add inline overrides with `STYLE (KEY = value, ...)`.

`ALLOW_MAXIMIZE` is a visual-level viewer option. Data and chart visuals show the maximize button by default. Input and control visuals (`SLICER`, `MULTISELECT`, `DATEPICKER`, `RELDATEPICKER`, `SLIDER`, `SEARCH`, `CHECKBOX`, `TEXTBOX`, `NUMBERBOX`) hide it by default.

## Options

- **BACKGROUND = '<color>'** — Background color fill for visual cards, containers, or buttons (e.g. `'#f8fafc'`, `'transparent'`). Also accepts `BACKGROUND_COLOR`.
- **BORDER = '<css-border>'** — Card or container border definition (e.g. `'1px solid #e2e8f0'`, `'none'`).
- **BORDER_RADIUS = '<css-radius>'** — Corner rounding for visual cards, containers, or buttons (e.g. `'8px'`, `'12px'`). Also accepts `BORDER-RADIUS`.
- **SHADOW = ON | OFF | '<css-shadow>'** — Card drop shadow. `ON` enables default elevation shadow, `OFF` disables shadow, or specify a custom CSS shadow string (e.g. `'0 4px 6px rgba(0,0,0,0.1)'`). Also accepts `BOX_SHADOW`.
- **COLOR = '<color>'** — Primary text color (e.g. `'#0f172a'`).
- **FONT = '<font-family>'** — Primary font family (e.g. `'Segoe UI'`, `'Inter, sans-serif'`).
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

## Examples

```sql
CREATE STYLE CorporateCard AS (
  THEME         = 'light',
  BACKGROUND    = '#ffffff',
  BORDER        = '1px solid #e2e8f0',
  BORDER_RADIUS = '10px',
  SHADOW        = ON
);

CREATE VISUAL RevChart AS BAR (
  SOURCE   = #revenue,
  MAPPINGS (X = month, Y = amount),
  TITLE    = 'Monthly Revenue',
  STYLE    = CorporateCard,
  STYLE    (ALLOW_MAXIMIZE = OFF)
);
```

## References

- [Report-SQL Guide](../../../guides/feature-guides/report-sql.md)
- [Custom Theming and Report Branding](../../../guides/reporting/custom-theming-and-branding.md)
- [THEME Reference](theme.md)
- [BUTTON Reference](button.md)
- [CONTAINER Reference](container.md)
- [Statements](../../statements/README.md)
