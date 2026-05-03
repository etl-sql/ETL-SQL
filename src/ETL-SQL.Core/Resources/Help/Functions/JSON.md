JSON functions work on JSON-typed variables or any string containing valid JSON. JSONPath uses $.dot.notation and $[0] for array indexing.

Extraction:
  JSON_VALUE(json, '$.path')        — extract a scalar value as a string
  JSON_QUERY(json, '$.path')        — extract an object or array fragment (returns JSON string)

Modification (returns updated copy; does not mutate):
  JSON_MODIFY(json, '$.path', val)  — set a value; use NULL val to remove a key

Validation:
  ISJSON(s)                         — 1 if s is valid JSON, 0 otherwise
  JSON_EXISTS(json, '$.path')       — 1 if the path exists, 0 otherwise

Table expansion:
  JSON_TABLE(json, '$.path')        — expand a JSON array into table rows
  OPENJSON(json [, '$.path'])       — SQL Server-style; columns: key, value, type
                                      type: 0=null, 1=string, 2=number, 3=bool, 4=array, 5=object

Serialization:
  SELECT ... FOR JSON PATH          — serialize query result to a JSON array
  SELECT ... FOR JSON PATH, ROOT('key')  — wrap in a root object

```sql
DECLARE @order JSON = '{
  "id": 42,
  "customer": {"name": "Alice", "tier": "Gold"},
  "items": [{"sku": "A1", "qty": 2}, {"sku": "B3", "qty": 1}]
}';

-- Scalar extraction
SELECT JSON_VALUE(@order, '$.id')                  -- '42'
SELECT JSON_VALUE(@order, '$.customer.name')        -- 'Alice'
SELECT JSON_VALUE(@order, '$.items[0].sku')         -- 'A1'

-- Object / array extraction
SELECT JSON_QUERY(@order, '$.customer')             -- '{"name":"Alice","tier":"Gold"}'
SELECT JSON_QUERY(@order, '$.items')                -- '[{"sku":"A1","qty":2},...]'

-- Modify
SET @order = JSON_MODIFY(@order, '$.customer.tier', 'Platinum');

-- Existence check
SELECT JSON_EXISTS(@order, '$.discount')            -- 0

-- Expand array to rows
SELECT key, value, type
FROM OPENJSON(JSON_QUERY(@order, '$.items'));

-- Expand with schema
SELECT sku, qty FROM OPENJSON(JSON_QUERY(@order, '$.items'))
WITH (sku VARCHAR '$.sku', qty INT '$.qty');

-- Serialize
SELECT id, name FROM #products FOR JSON PATH;
-- [{"id":1,"name":"Widget"},{"id":2,"name":"Gadget"}]
```
