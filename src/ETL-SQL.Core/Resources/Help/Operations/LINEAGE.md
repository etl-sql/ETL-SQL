# LINEAGE
Attaches metadata tags to data as it flows through the engine. Tags are queryable via SHOW TAGS and can be reported to downstream governance tools.

Syntax:
  TAG <source_expression> WITH (Key = 'Value', ...);
  SHOW TAGS [INTO #table];

Keys are free-form strings. Common conventions: Source, Owner, Classification, Department, SLA.

```sql
-- Tag a raw load
SELECT * INTO #raw FROM SourceDB.dbo.Transactions;
TAG #raw WITH (Source = 'SourceDB', Classification = 'Confidential', Owner = 'Finance');

-- Tag the transformed output
SELECT account, SUM(amount) AS total INTO #summary FROM #raw GROUP BY account;
TAG #summary WITH (Source = 'ETL-SQL', Department = 'Finance', SLA = '4h');

-- View all tags in the session
SHOW TAGS INTO #all_tags;
SELECT * FROM #all_tags;
```
