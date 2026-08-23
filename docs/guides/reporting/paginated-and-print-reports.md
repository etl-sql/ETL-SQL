# Paginated and Print-Ready Reports

While `DASHBOARD` pages provide responsive, single-screen layouts designed for browser interaction, `PAGINATED` pages are designed for multi-page documents, invoices, formal statements, and pixel-precise physical printing or PDF export.

---

> **Applies to:** every deployment profile (Solo, Team, Enterprise, SaaS).

## Dashboard vs. Paginated Pages

| Feature | `CREATE PAGE ... AS DASHBOARD` | `CREATE PAGE ... AS PAGINATED` |
| :--- | :--- | :--- |
| **Target Medium** | Web browser / Screen display | Physical Paper (Letter, A4) / PDF Export |
| **Grid Model** | Fluid CSS Grid (`STRUCTURE = 'A B / C D'`) | Physical page layout with margins and headers |
| **Execution Model** | Interactive: slicers update visuals immediately | Staged: prompts stage inputs until "Run" is clicked |
| **Table Behavior** | In-browser scrolling and virtual paging | Physical page splitting with repeated headers |

---

## Example 1: Multi-Page Monthly Invoice (Letter Size)

This example sets standard `Letter` dimensions, adds a page break before the summary card, and excludes prompt controls from the final print/PDF output.

```sql
SET REPORT TITLE = 'Monthly Client Invoice';

DECLARE @clientId INT INPUT = 101;

CREATE CONNECTION db AS MOCKDB();

SELECT ClientId, ClientName, InvoiceDate, BalanceDue
INTO #invoice_meta
FROM db.Clients;

SELECT ItemId, Description, Quantity, UnitPrice, TotalPrice
INTO #line_items
FROM db.InvoiceItems;

-- Visuals
CREATE VISUAL InvoiceHeader AS CARD (
  SOURCE   = (SELECT ClientName, 'Client' AS Lbl FROM #invoice_meta WHERE ClientId = @clientId),
  MAPPINGS (VALUE = ClientName, LABEL = Lbl)
);

CREATE VISUAL ItemsTable AS TABLE (
  SOURCE = #line_items,
  SUMMARY (
    GRAND_TOTAL = ON,
    SUM(TotalPrice) AS 'Invoice Total'
  )
);

-- Force page break before invoice summary and keep together
CREATE VISUAL InvoiceSummary AS CARD (
  SOURCE   = (SELECT BalanceDue, 'Total Due' AS Lbl FROM #invoice_meta WHERE ClientId = @clientId),
  MAPPINGS (VALUE = BalanceDue, LABEL = Lbl),
  PRINT_LAYOUT (
    PAGE_BREAK_BEFORE = ON,
    KEEP_TOGETHER     = ON
  )
);

-- Paginated Page Definition
CREATE PAGE InvoiceDoc AS PAGINATED (
  LAYOUT (
    STRUCTURE = 'A / B / C',
    MAP (
      'A' = InvoiceHeader,
      'B' = ItemsTable,
      'C' = InvoiceSummary
    )
  ),
  PRINT_LAYOUT (
    PAGE_SIZE   = 'Letter',
    ORIENTATION = 'PORTRAIT',
    MARGINS     = (0.75, 0.75, 0.75, 0.75),
    UNITS       = 'in',
    OVERFLOW    = 'AUTO'
  )
);
```

---

## Example 2: The "Run-to-Data" Deferred Query Pattern

For heavy analytical queries, prevent the report from re-running on every keystroke. Use a `PAGINATED` page where parameter changes are staged until an `APPLY_PARAMETERS` button is clicked.

```sql
SET REPORT TITLE = 'Financial Statement Run';

DECLARE @asOfDate DATE INPUT = '2026-06-30';
DECLARE @dept     VARCHAR INPUT = 'All';

CREATE CONNECTION db AS MOCKDB();

SELECT Department, Account, Balance
INTO #ledger
FROM db.GeneralLedger;

-- Datepicker prompt: excluded from printed document
CREATE VISUAL DatePrompt AS DATEPICKER (
  ACTIONS (ON_CHANGE = SET_PARAMETER(@asOfDate, value)),
  PRINT_LAYOUT (EXCLUDE_FROM_PRINT = ON)
);

-- Execute Button: applies staged parameter changes
CREATE BUTTON RunButton AS (
  TITLE   = 'Generate Statement',
  ACTIONS (ON_CLICK = APPLY_PARAMETERS),
  STYLE   (BACKGROUND = '#2563eb', COLOR = '#ffffff', FONT_WEIGHT = 'BOLD')
);

-- Result visual: runs only after parameters are applied
CREATE VISUAL StatementTable AS TABLE (
  SOURCE = (SELECT Department, Account, Balance
            FROM #ledger
            WHERE @dept = 'All' OR Department = @dept)
);

CREATE PAGE StatementPage AS PAGINATED (
  LAYOUT (
    STRUCTURE = 'A B / C C',
    MAP (
      'A' = DatePrompt,
      'B' = RunButton,
      'C' = StatementTable
    )
  ),
  PRINT_LAYOUT (
    PAGE_SIZE   = 'A4',
    ORIENTATION = 'LANDSCAPE'
  )
);
```

---

## Automatic Table Splitting

When a `TABLE` visual contains more rows than can fit on a single physical sheet, the engine's `PhysicalPageCompiler`:
1. Automatically measures row height and page margin boundaries.
2. Slices table rows into consecutive physical sheets (`startRowIndex` to `endRowIndex`).
3. Re-prints the table column headers at the top of every subsequent physical page.

---

## Common Pitfalls

- **Missing `APPLY_PARAMETERS` on paginated prompts**: On a `PAGINATED` page, prompt inputs stage their changes locally. If you do not provide a button with `ACTIONS (ON_CLICK = APPLY_PARAMETERS)`, the report will not re-execute when users select new values.
- **Overcrowded Margins**: Specifying margins smaller than `0.25in` (or `6mm`) can lead to content clipping on physical printer hardware boundaries.

---

## Related Topics

- [Authoring Dashboards](authoring-dashboards.md) — Single-screen fluid dashboard layouts.
- [Report Parameters and Filters](report-parameters-and-filters.md) — Interactive controls and variables.
- [PRINT_LAYOUT Reference](../../reference/statements/ddl/print_layout.md) — Complete property reference.
