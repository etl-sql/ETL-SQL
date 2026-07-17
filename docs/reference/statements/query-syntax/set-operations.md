# Set Operations


```sql
SELECT region FROM #east_sales
UNION
SELECT region FROM #west_sales;

SELECT id FROM #batch_a
UNION ALL
SELECT id FROM #batch_b;

SELECT id FROM #full_list
EXCEPT
SELECT id FROM #processed;

SELECT id FROM #active
INTERSECT
SELECT id FROM #eligible;

-- MINUS is an alias for EXCEPT
SELECT id FROM #full_list
MINUS
SELECT id FROM #processed;

-- UNION [ALL] BY NAME aligns inputs by column name (not position); missing columns become NULL
SELECT 1 AS a, 2 AS b
UNION BY NAME
SELECT 20 AS b, 10 AS a;          -- columns a, b -> (1,2), (10,20)

SELECT 1 AS a, 2 AS b
UNION ALL BY NAME
SELECT 3 AS a;                    -- (1,2), (3, NULL)
```

## References

- [Statement Reference](../README.md)
- [Syntax Index](../../../syntax-index.md)

