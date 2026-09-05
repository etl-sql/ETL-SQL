# BUTTON

An interactive button that triggers navigation, dialog control, parameter updates, or report refreshes when clicked.

## Syntax

```sql
CREATE BUTTON <name> AS (
  [TITLE = 'label',]
  [OPTIONS (
    [VARIANT = PRIMARY|SECONDARY|GHOST|DANGER|LINK,]
    [ICON = '<icon_name>',]
    [ICON_POSITION = LEFT|RIGHT,]
    [DISABLED = '<expression>',]
    [SHOW_SPINNER = ON|OFF,]
    [MODE = TOGGLE,]
    [ON_VALUE = '<value>',]
    [OFF_VALUE = '<value>',]
    [CONFIRM = '<prompt_message>']
  ),]
  ACTIONS (ON_CLICK = <action> | (<action>, ...))
);
```

## Options

- **VARIANT = PRIMARY|SECONDARY|GHOST|DANGER|LINK** — Semantic button appearance and style hierarchy (default `SECONDARY`).
- **ICON = 'name'** — Icon name rendered on the button.
- **ICON_POSITION = LEFT|RIGHT** — Position of icon relative to button label text (default `LEFT`).
- **DISABLED = 'expression'** — Dynamic expression evaluated against parameters to disable button interaction.
- **SHOW_SPINNER = ON|OFF** — Shows an animated loading spinner while async click actions are running.
- **MODE = TOGGLE** — Configures button as a toggle switch alternating between `ON_VALUE` and `OFF_VALUE`.
- **ON_VALUE = 'value'** — Parameter value assigned when toggle button is active.
- **OFF_VALUE = 'value'** — Parameter value assigned when toggle button is inactive.
- **CONFIRM = 'message'** — Prompts user with a confirmation modal before firing click actions.

## Actions

- **ON_CLICK = RESET_PARAMETERS([@param, ...])** — Resets all or specified parameters to their default values.
- **ON_CLICK = OPEN_URL('url' [, TARGET = '_blank|_self'])** — Opens an external web URL.
- **ON_CLICK = OPEN_URL(TEMPLATE = 'url-with-{Field}-placeholders', PARAMS = (col, ...) [, TARGET = '_blank|_self'])** — Opens a URL built from the clicked row. Only the columns listed in `PARAMS` may be interpolated, and every value is URL-encoded, so a row value cannot introduce a path segment, query parameter, or scheme of its own. A placeholder naming an undeclared or missing column resolves to the empty string. URL templates are author-declared, so no path resolution policy applies.
- **ON_CLICK = SHOW_MODAL('ModalName')** — Displays the specified MODAL container.
- **ON_CLICK = HIDE_MODAL('ModalName')** — Dismisses and hides the specified MODAL container.
- **ON_CLICK = BACK** — Navigates back to the previous report page.
- **ON_CLICK = REFRESH_REPORT** — Re-evaluates report queries and refreshes all visuals.
- **ON_CLICK = REFRESH_VISUALS(Visual [, ...])** — Re-evaluates and refreshes specific visuals.
- **ON_CLICK = SET_PARAMETER(@var, value)** — Updates a session variable and triggers reactive dependencies.
- **ON_CLICK = CLEAR_FILTERS** — Clears active visual filter selections.
- **ON_CLICK = APPLY_PARAMETERS** — Commits staged parameter selections.
- **ON_CLICK = NAVIGATE_PAGE(PageName)** — Switches visible view to the specified page.
- **ON_CLICK = SET_UI_STATE(Target, Key, Value)** — Adjusts visibility, collapse, or styling states of report elements.
- **ON_CLICK = APPLY_BOOKMARK(BookmarkName)** — Restores saved parameter, page, and UI bookmark states.
- **ON_CLICK = (Action1, Action2, ...)** — Executes a sequential series of actions on click.

## Examples

```sql
CREATE BUTTON CommitBtn AS (
  TITLE = 'Apply Changes',
  OPTIONS (
    VARIANT = PRIMARY,
    ICON = 'check',
    CONFIRM = 'Apply all parameter updates to the active session?'
  ),
  ACTIONS (
    ON_CLICK = (
      APPLY_PARAMETERS,
      REFRESH_REPORT
    )
  )
);
```

```sql
CREATE BUTTON ResetBtn AS (
  TITLE = 'Reset Filters',
  OPTIONS (
    VARIANT = GHOST,
    ICON = 'refresh-cw'
  ),
  ACTIONS (
    ON_CLICK = RESET_PARAMETERS(@region, @start_date)
  )
);
```

## Lifecycle

```sql
CREATE OR REPLACE BUTTON CommitBtn AS (...);   -- redefine from scratch
ALTER BUTTON CommitBtn (TITLE = 'Save');       -- patch named clauses only
DROP BUTTON IF EXISTS CommitBtn;
```

## References

- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
