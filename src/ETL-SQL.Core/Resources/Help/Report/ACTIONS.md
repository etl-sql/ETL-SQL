# ACTIONS
Interactive charts, tables, controls, and buttons can trigger one or more actions when a user interacts with them.

Syntax:
```sql
ACTIONS (
  ON_CLICK = <action>
)
```

## Supported Triggers
| Object type | Valid trigger | Description |
|-------------|---------------|-------------|
| Charts and tables | `ON_CLICK` | Fires when the user clicks a chart element, point, map region, or table row. |
| Controls | `ON_CHANGE` | Applies to `SLICER`, `MULTISELECT`, `DATEPICKER`, `RELDATEPICKER`, `SLIDER`, `SEARCH`, `CHECKBOX`, `TEXTBOX`, and `NUMBERBOX`. |
| Buttons | `ON_CLICK` | Fires when the button is clicked. |
| `TEXT`, `CARD`, `IMAGE` | none | Display-only visuals do not accept `ACTIONS`; use `CREATE BUTTON` for clickable behavior. |

Invalid trigger/object combinations are syntax errors.

## Supported Actions

### SET_PARAMETER
Updates an ETL-SQL variable and re-evaluates the dependent visuals.
```sql
ON_CHANGE = SET_PARAMETER(@category, value)
```

### DRILL_DOWN
Updates a target visual by passing a filter value.
```sql
ON_CLICK = DRILL_DOWN(Target = DetailChart, Key = Region)
-- Composite key:
ON_CLICK = DRILL_DOWN(Target = DetailChart, Key = (Region, Product))
```

### DRILL_IN
Enables in-place hierarchical drill-down on the same visual. Click a bar to go deeper; click a breadcrumb to navigate back up.
```sql
ON_CLICK = DRILL_IN(HIERARCHY = (Year, Quarter, Month))
```
The SOURCE query must include all hierarchy columns. The runtime regroups and re-aggregates at each level automatically.

### RUN_SCRIPT
Executes a custom ETL-SQL script file on the server.
```sql
ON_CLICK = RUN_SCRIPT('scripts/export_to_csv.etlsql', @p1 = col1, @p2 = 'StaticValue')
```

### CLEAR_FILTERS
Clears visual-level selections and returns cross-filtered visuals to their unfiltered state.
```sql
ON_CLICK = CLEAR_FILTERS
```

### SET_UI_STATE
Changes the visual state of report objects (Visibility, Color, Class) without a server round-trip.
```sql
ON_CLICK = SET_UI_STATE('FilterPanel', 'VISIBLE', OFF)
-- Toggle class:
ON_CLICK = SET_UI_STATE('TargetVisual', 'CLASS', '+highlighted')
```
Key: `VISIBLE`, `COLLAPSED`, `COLOR`, `BACKGROUND-COLOR`, `CLASS`.
Value: `ON`/`OFF`, hex colors, or class names (prefix with `+` to add, `-` to remove).

## Examples

**Table with Row Selection:**
```sql
CREATE VISUAL OrdersTable AS TABLE (
  SOURCE = #orders,
  ACTIONS (ON_CLICK = SET_PARAMETER(@selected_order, order_id))
);
```

**Slicer driving a variable:**
```sql
CREATE VISUAL RegionSlicer AS SLICER (
  SOURCE = (SELECT DISTINCT Region FROM #data),
  MAPPINGS (VALUE = Region),
  ACTIONS (ON_CHANGE = SET_PARAMETER(@region, Region))
);
```
