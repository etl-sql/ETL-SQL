Type: TABLE
A paginated, sortable data table. Columns are driven by the SOURCE query — every column in the SELECT becomes a table column by default. Use MAPPINGS to rename, reorder, or restrict columns.

Mappings:
  List the column names from the source to include; use AS to rename:
    MAPPINGS (order_id AS "Order #", customer, amount AS "Total ($)")

Options:
  PAGE_SIZE    = n         — rows per page (default 50; 0 = no pagination)
  STRIPED      = ON|OFF    — alternating row background colours (default ON)
  GRAND_TOTAL  = SUM|AVG|COUNT  — footer row with the chosen aggregate per numeric column
  SUMMARIZE_ROW = ON|OFF   — show a totals row at the top instead of the footer
  SEARCH       = ON|OFF    — client-side search box above the table (default ON)
  FONT_SIZE    = n         — table body font size in pixels

Column-level formatting (inside MAPPINGS):
  column FORMAT '.NET format'  — e.g.  amount FORMAT 'C2',  pct FORMAT 'P1'
  column ALIGN  'left'|'center'|'right'

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
    amount        FORMAT 'C2' AS "Amount",
    status
  ),
  OPTIONS  (
    PAGE_SIZE   = 25,
    GRAND_TOTAL = SUM,
    SEARCH      = ON,
    TITLE       = 'Orders (last 90 days)'
  )
);
```
