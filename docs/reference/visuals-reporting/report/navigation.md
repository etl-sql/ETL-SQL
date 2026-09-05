# NAVIGATION

Creates a menu or tab strip that links multiple report pages, providing user navigation between views with support for icons, badges, collapsible groups, and external links.

## Syntax

```sql
CREATE NAVIGATION <name> AS TAB | BUTTON | LINK (
  [ORIENTATION = HORIZONTAL | VERTICAL,]
  [DEFAULT = <pageName>,]
  [HIDE_INVISIBLE = ON | OFF,]
  [PAGES (
    <page1> [(ICON = '<icon_name>', LABEL = '<display_label>', BADGE = '<badge_text>')],
    ...
  ),]
  [GROUP ('<section_title>' = (<page_a>, <page_b>, ...)), ...]
  [LINK ('<label>' = OPEN_URL('<url>' [, TARGET = '_blank'])), ...]
  [STYLE (KEY = value, ...)]
  [ACTIVE_STYLE (KEY = value, ...)]
);
```

## Navigation Types

- **`TAB`** — Horizontal or vertical tab strip (default for dashboards).
- **`BUTTON`** — A segmented button bar for switching views.
- **`LINK`** — Inline breadcrumb or text link list.

## Configuration Options

- **`ORIENTATION = HORIZONTAL | VERTICAL`** — Display direction of the navigation container (default `HORIZONTAL`).
- **`DEFAULT = <pageName>`** — Page to show on initial report load.
- **`HIDE_INVISIBLE = ON | OFF`** — When set to `ON`, navigation items whose target `PAGE` is hidden (via `VISIBLE = OFF` or falsy expression) are automatically suppressed from the navigation bar (default `OFF`).
- **`PAGES (...)`** — List of report pages included in the navigation bar:
  - **`ICON = '<name>'`** — Icon identifier or asset path rendered next to the item label.
  - **`LABEL = '<text>'`** — Custom display text overriding the default page name.
  - **`BADGE = '<text>'`** — Notification badge or pill rendered beside the item (e.g. `'New'` or `'5'`).
- **`GROUP ('<title>' = (...))`** — Groups related pages into collapsible or sectioned navigation groups.
- **`LINK ('<label>' = OPEN_URL('<url>'))`** — Adds an external hyperlink directly to the navigation bar.
- **`STYLE (...)`** — Base CSS styling overrides for navigation items (e.g. `COLOR`, `FONT_SIZE`, `PADDING`).
- **`ACTIVE_STYLE (...)`** — CSS styling applied specifically to the currently active page item (e.g. `COLOR = '#2563eb'`, `FONT_WEIGHT = 'bold'`).

## Examples

```sql
CREATE PAGE Overview AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = SummaryChart));
CREATE PAGE Reports AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = DetailTable));
CREATE PAGE Admin AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = UserAdminTable), VISIBLE = @IsAdmin);

CREATE NAVIGATION MainNav AS TAB (
  ORIENTATION = HORIZONTAL,
  DEFAULT = Overview,
  HIDE_INVISIBLE = ON,
  PAGES (
    Overview (ICON = 'dashboard', LABEL = 'Dashboard Home'),
    Reports (ICON = 'table', LABEL = 'Detailed Reports', BADGE = 'Updated'),
    Admin (ICON = 'shield', LABEL = 'Administration')
  ),
  GROUP ('Documentation' = (HelpPage, FaqPage)),
  LINK ('Support Portal' = OPEN_URL('https://support.example.com')),
  STYLE (COLOR = '#4b5563'),
  ACTIVE_STYLE (COLOR = '#2563eb', FONT_WEIGHT = '600')
);
```

## Lifecycle

```sql
CREATE OR REPLACE NAVIGATION MainNav AS TAB (...);
DROP NAVIGATION IF EXISTS MainNav;
```

References:
- [PAGE Reference](page.md)
- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
