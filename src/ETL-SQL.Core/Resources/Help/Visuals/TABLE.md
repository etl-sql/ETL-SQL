Type: TABLE
A paginated, sortable data table. Columns are driven by the SOURCE query — every column in the SELECT becomes a table column by default. Use MAPPINGS to rename, reorder, restrict, and format columns.

Mappings (column-level, inside MAPPINGS block):
  column_name                                    — include column with its original name
  column_name AS 'Display Name'                  — rename the column header
  column_name FORMAT 'C2' AS 'Amount'            — apply .NET number format and optional rename
  column_name ALIGN 'right'                      — set column text alignment (left|center|right)
  column_name DATA_BAR                           — show a proportional fill bar behind cell value
  column_name DATA_BAR COLOR '#4472C4'           — data bar with custom color
  column_name COLOR_SCALE FROM '#FF0000' TO '#00FF00'  — gradient cell background (min→max)
  column_name IMAGE [WIDTH n]                    — render cell value as an <img> (URL column)
  column_name HYPERLINK [LABEL 'text']           — render cell value as a clickable link
  SPARKLINE(col1, col2, ...) [LINE|BAR|AREA] [AS 'alias']  — mini trend chart across columns
  Combinable: column FORMAT 'C2' ALIGN 'right' DATA_BAR COLOR '#4472C4' AS 'Revenue'

  MAPPINGS restricts visible columns to those listed (in that order).
  If MAPPINGS is omitted, all SELECT columns are shown.

Options:
  PAGE_SIZE    = n         — rows per page (default 50; 0 = no pagination)
  STRIPED      = ON|OFF    — alternating row background colours (default ON)
  GRAND_TOTAL  = SUM|AVG|COUNT  — footer row with the chosen aggregate per numeric column
  SUMMARIZE_ROW = ON|OFF   — show a totals row at the top instead of the footer
  SEARCH       = ON|OFF    — client-side search box above the table (default ON)
  FONT_SIZE    = n         — table body font size in pixels

Sorting:
  Column headers are clickable — click to sort ascending, click again to sort descending.

Conditional row formatting:
  FORMATTING (
    WHEN condition THEN 'bg-color' [FONT_COLOR 'text-color'],
    ...
  )

```sql
SELECT
    order_id,
    customer_name,
    order_date,
    amount,
    status,
    logo_url,
    product_url,
    jan, feb, mar, apr, may, jun
INTO #orders_view
FROM prod.Orders
WHERE order_date >= DATEADD(DAY, -90, GETDATE())
ORDER BY order_date DESC;

CREATE VISUAL RecentOrders AS TABLE (
  SOURCE   = #orders_view,
  MAPPINGS (
    order_id      AS "Order #",
    customer_name AS "Customer",
    order_date    AS "Date",
    amount        FORMAT 'C2' ALIGN 'right' DATA_BAR COLOR '#4472C4' AS "Amount",
    status        COLOR_SCALE FROM '#FFE0E0' TO '#D4EDDA',
    logo_url      IMAGE WIDTH 24 AS "Logo",
    product_url   HYPERLINK LABEL 'View' AS "Link",
    SPARKLINE(jan, feb, mar, apr, may, jun) LINE AS "6-Month Trend"
  ),
  FORMATTING (
    WHEN amount > 10000 THEN '#d4edda' FONT_COLOR '#155724',
    WHEN amount < 100   THEN '#f8d7da' FONT_COLOR '#721c24'
  ),
  OPTIONS  (
    PAGE_SIZE   = 25,
    GRAND_TOTAL = SUM,
    SEARCH      = ON,
    STRIPED     = ON
  )
);
```

References:
- [Report SQL Guide](../../../../../Docs/Report_SQL_Guide.md)
