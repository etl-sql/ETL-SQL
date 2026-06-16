# TABLE

A paginated, sortable data table. Columns are driven by the `SOURCE` query — every column in the `SELECT` becomes a table column by default. Use `MAPPINGS` to rename, reorder, restrict, and format columns.

## Syntax

```sql
CREATE VISUAL RecentOrders AS TABLE (
  SOURCE   = #orders_view,
  MAPPINGS (
    order_id      AS "Order #",
    amount        FORMAT 'C2' ALIGN 'right' DATA_BAR COLOR '#4472C4' AS "Amount"
  ),
  FORMATTING (
    WHEN amount > 10000 THEN '#d4edda' FONT_COLOR '#155724'
  ),
  OPTIONS (
    PAGE_SIZE = 25,
    SEARCH    = ON
  )
);
```

## Mappings (Inside MAPPINGS block)

- **`column_name`**: Include column with its original name.
- **`column_name AS 'Display Name'`**: Rename the column header.
- **`column_name FORMAT 'C2' AS 'Amount'`**: Apply .NET number format and optional rename.
- **`column_name ALIGN 'right'`**: Set column text alignment (`left`|`center`|`right`).
- **`column_name DATA_BAR`**: Show a proportional fill bar behind cell value.
- **`column_name DATA_BAR COLOR '#4472C4'`**: Data bar with custom color.
- **`column_name COLOR_SCALE FROM '#FF0000' TO '#00FF00'`**: Gradient cell background (min→max).
- **`column_name IMAGE [WIDTH n]`**: Render cell value as an `<img>` tag (URL column).
- **`column_name HYPERLINK [LABEL 'text']`**: Render cell value as a clickable link.
- **`SPARKLINE(col1, col2, ...) [LINE|BAR|AREA] [AS 'alias']`**: Mini trend chart across columns.

*Note: Mappings are combinable, e.g.:*  
`column FORMAT 'C2' ALIGN 'right' DATA_BAR COLOR '#4472C4' AS 'Revenue'`

- `MAPPINGS` restricts visible columns to those listed (in that order).
- If `MAPPINGS` is omitted, all `SELECT` columns are shown.

## Options

- **`PAGE_SIZE = n`**: Rows per page (default `50`; `0` = no pagination).
- **`STRIPED = ON|OFF`**: Alternating row background colours (default `ON`).
- **`GRAND_TOTAL = SUM|AVG|COUNT`**: Footer row with the chosen aggregate per numeric column.
- **`SUMMARIZE_ROW = ON|OFF`**: Show a totals row at the top instead of the footer.
- **`SEARCH = ON|OFF`**: Client-side search box above the table (default `ON`).
- **`FONT_SIZE = n`**: Table body font size in pixels.

## Sorting

Column headers are clickable — click to sort ascending, click again to sort descending.

## Conditional Row Formatting

```sql
FORMATTING (
  WHEN condition THEN 'bg-color' [FONT_COLOR 'text-color'],
  ...
)
```

## References
- [Report SQL Guide](../../../../../Docs/Report_SQL_Guide.md)
