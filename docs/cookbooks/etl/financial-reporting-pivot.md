# Financial Reporting (PIVOT)
<!-- description: **▶ Runs as-is.** Rotate a vertical transaction log into a horizontal summary with `PIVOT` for executive reporting. -->
Rotate vertical transaction logs into a horizontal quarterly summary for executive reporting.

## Try It Now (No Database Needed)

Paste into a `.etlsql` file and press **F5**. Revenue from the built-in `MOCKDB()` orders is pivoted by region for each product category:

```sql
CREATE CONNECTION demo AS MOCKDB();

-- 1. Vertical log: revenue per product category and region
SELECT p.Category, o.Region, o.Total
INTO #sales
FROM demo.Orders AS o
JOIN demo.Products AS p ON p.ProductID = o.ProductID;

-- 2. Rotate regions into columns
SELECT Category, [North America], [EMEA], [APAC], [LATAM]
INTO #report
FROM (SELECT Category, Region, Total FROM #sales) AS src
PIVOT (SUM(Total) FOR Region IN ([North America], [EMEA], [APAC], [LATAM])) AS pvt;

SELECT Category, [North America], [EMEA], [APAC], [LATAM]
FROM #report
ORDER BY Category;
```

Each region becomes a column. The production version pivots by quarter and exports to Excel:

## Production Version

```sql
-- Pivot rows to columns
SELECT Category, [Q1], [Q2], [Q3], [Q4]
INTO #Report
FROM (SELECT Category, Quarter, Amount FROM #MonthlySales) AS src
PIVOT (SUM(Amount) FOR Quarter IN ([Q1], [Q2], [Q3], [Q4])) AS pvt;

-- Export to Excel — always create a named connection first
CREATE CONNECTION xl_out AS EXCEL('C:\Reports\Quarterly_Summary.xlsx', HEADER=ON);
INSERT INTO xl_out SELECT * FROM #Report;

PRINT 'Quarterly report exported.';
```
