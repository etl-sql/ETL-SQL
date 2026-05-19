Type: TABLE
A paginated, sortable data table. Columns are driven by the SOURCE query — every column in the SELECT becomes a table column by default. Use MAPPINGS to rename, reorder, restrict, and format columns.

Mappings (column-level, inside MAPPINGS block):
  column_name                         — include column with its original name
  column_name AS 'Display Name'       — rename the column header
  column_name FORMAT 'C2' AS 'Amount' — apply .NET number format and optional rename
  column_name ALIGN 'right'           — set column text alignment (left|center|right)
  Combinable: column FORMAT 'P1' ALIGN 'right' AS 'Pct'

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
    status
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
    amount        FORMAT 'C2' ALIGN 'right' AS "Amount",
    status
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
