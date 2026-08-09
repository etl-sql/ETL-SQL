# Importing Curated Lineage & Tags (Non-Standard Sources)
When column documentation, ownership, or lineage already lives in your own catalog — a spreadsheet, a governance tool, or a previous run's OpenLineage export — you can project it into the engine instead of re-deriving it. Tags seeded before a transform flow onto derived columns automatically via lineage inheritance.

**Pattern Scenario:** Seed in-house column tags from a catalog table, run a transform, then re-import a previously exported OpenLineage document.

```sql
-- 1. Project an in-house metadata catalog into the engine as tags.
--    Pretend these rows come from your own data-catalog tables.
CREATE TABLE #column_docs (tbl VARCHAR(50), col VARCHAR(50), descr VARCHAR(200), owner VARCHAR(50));
INSERT INTO #column_docs VALUES
    ('Orders', 'Amount',    'Gross sale amount in USD',      'Finance'),
    ('Orders', 'OrderDate', 'Timestamp the sale was placed', 'Sales');

-- Loop the catalog and seed tags BEFORE the transform, so derived columns inherit them.
FOR @r IN (SELECT tbl, col, descr, owner FROM #column_docs)
BEGIN
    INSERT TAG FOR TABLE @r.tbl COLUMN @r.col (d = @r.descr, owner = @r.owner);
END

-- 2. A normal transform. Tags seeded above ride along onto #daily.Revenue.
CREATE TABLE Orders (OrderId INT, Amount INT, OrderDate DATETIME);
INSERT INTO Orders VALUES (1, 100, '2024-01-01'), (2, 250, '2024-01-02');

SELECT Amount AS Revenue, OrderDate INTO #daily FROM Orders;
SELECT TagName, TagValue
FROM eng.tags
WHERE TargetTable = '#daily' AND TargetColumn = 'Revenue';

-- 3. Round-trip lineage through an OpenLineage document. In production the file
--    would come from a prior run or an upstream system rather than this export.
EXPORT LINEAGE AS OPENLINEAGE TO 'output/daily_lineage.json';
INSERT LINEAGE FOR TABLE #daily FROM 'output/daily_lineage.json';
EXPORT LINEAGE FOR #daily AS OPENLINEAGE TO 'output/daily_lineage_report.json';
```

> Imports are a starting point, not a freeze: any lineage the script produces afterwards accrues on top (last-writer-wins). See [Reference/Lineage.md](../../reference/statements/session-control/lineage.md) for the tag/lineage import grammar.
