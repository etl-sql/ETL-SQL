# LATERAL

Correlated subquery join. Allows the right-hand subquery or table-valued expression to reference columns from preceding tables on its left. Evaluates dynamically for each left row, functioning like an in-engine relational `FOREACH`.

---

## Syntax

```sql
-- Inner correlated join (equivalent to CROSS APPLY):
SELECT <columns...>
FROM <left_table> AS l
CROSS JOIN LATERAL (<subquery_referencing_l>) AS r;

-- Outer correlated join (equivalent to OUTER APPLY):
SELECT <columns...>
FROM <left_table> AS l
LEFT JOIN LATERAL (<subquery_referencing_l>) AS r ON TRUE;
```

---

## Correlation Mechanics & APPLY Mapping

| LATERAL Syntax Form | Equivalent T-SQL Form | Behavior on Zero Subquery Matches |
| :--- | :--- | :--- |
| `CROSS JOIN LATERAL (...)` | `CROSS APPLY (...)` | Drops left row if subquery returns 0 rows |
| `LEFT JOIN LATERAL (...) ON TRUE` | `OUTER APPLY (...)` | Preserves left row; fills right subquery columns with `NULL` |
| `INNER JOIN LATERAL (...) ON <cond>` | `CROSS APPLY (...)` + `WHERE <cond>` | Matches correlated rows and applies post-join filter |

---

## Examples

### 1. Top-N Records Per Group (Most Recent Order Lines)

Fetch the single latest line item and high-value status for every customer order:

```sql
SELECT 
    o.order_id, 
    o.customer_id, 
    o.order_date,
    recent_item.item_sku, 
    recent_item.unit_price, 
    recent_item.quantity
FROM #orders AS o
LEFT JOIN LATERAL (
    SELECT item_sku, unit_price, quantity
    FROM #order_items
    WHERE order_id = o.order_id
    ORDER BY line_number DESC
    LIMIT 1
) AS recent_item ON TRUE;
```

### 2. Production ETL: JSON Array Expansion & Nested Entity Flattening

Extract multi-tenant webhook event logs and expand variable-length JSON arrays into normalized relational staging tables:

```sql
CREATE CONNECTION src AS POSTGRES(HOST='events.internal', DATABASE='webhooks');
CREATE CONNECTION dw  AS MSSQL(SERVER='dw.internal', DATABASE='analytics');

-- 1. Extract raw payloads containing nested tag arrays
SELECT event_id, tenant_id, payload, created_at
INTO #raw_events
FROM src.inbound_events
WHERE created_at >= DATEADD(HOUR, -6, GETDATE());

-- 2. Flatten nested tags per event using LATERAL UNNEST
SELECT 
    e.event_id,
    e.tenant_id,
    e.created_at,
    tag.value AS tag_name
INTO #event_tags_staged
FROM #raw_events AS e
CROSS JOIN LATERAL UNNEST(JSON_GET_ARRAY(e.payload, '$.tags')) AS tag;

-- 3. Load normalized event tags into warehouse dimensional model
INSERT INTO dw.dbo.FactEventTags (EventId, TenantId, CreatedAt, TagName)
SELECT event_id, tenant_id, created_at, tag_name FROM #event_tags_staged;
```

---

## Remarks & Best Practices

- **Explicit ON Clause**: Unlike standard `APPLY`, `LEFT JOIN LATERAL` supports an explicit `ON <condition>`. The idiomatic syntax when filtering is encapsulated within the subquery is `ON TRUE`.
- **Performance**: Ensure correlation columns (e.g. `order_id` in the subquery's `WHERE` clause) are indexed or filtered on indexed `#temp` tables to maintain fast execution across large left-side inputs.

---

## References & Related Recipes

- [Query Syntax Reference](README.md)
- [SELECT Statement](../dml/select.md)
- [UNNEST Statement](../dml/unnest.md)
- [ETL Cookbook: Master-Detail Drill-Through](../../../cookbooks/etl/master-detail-drill-through.md)
- [ETL Cookbook: Multi-Context Join](../../../cookbooks/etl/multi-context-join.md)
- [Syntax Index](../../../syntax-index.md)
