# Financial Reporting (PIVOT)
Rotate vertical transaction logs into a horizontal quarterly summary for executive reporting.

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
