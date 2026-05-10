# STYLE
Defines a reusable visual theme that can be referenced by pages and visuals to apply consistent formatting.

Syntax:
  CREATE STYLE <name> (
    THEME      = 'light' | 'dark',
    BACKGROUND = '#rrggbb',
    FONT       = 'font-name',
    FONT_SIZE  = n,
    COLORS     = ('#color1', '#color2', ...),
    BORDER     = 'css-border-value'
  );

Apply a named style with `STYLE = StyleName` on a visual, page, container, or button. Add inline overrides with `STYLE (KEY = value, ...)`.

```sql
CREATE STYLE Corporate (
  THEME      = 'light',
  BACKGROUND = '#f8f9fa',
  FONT       = 'Segoe UI',
  FONT_SIZE  = 13,
  COLORS     = ('#0070c0', '#00b050', '#ff0000', '#ffc000')
);

CREATE VISUAL RevChart AS BAR (
  SOURCE   = #revenue,
  MAPPINGS (X = month, Y = amount),
  TITLE    = 'Monthly Revenue',
  STYLE    = Corporate
);
```
