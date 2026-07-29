# eng.views

`eng.views` lists view definitions registered in the current session.

## Query

```sql
SELECT view_name, query
FROM eng.views
ORDER BY view_name;
```

## Columns

| Column | Description |
| :--- | :--- |
| `view_name` | Session view name. |
| `query` | Serialized query definition for the view. |

## Example

```sql
SELECT view_name
FROM eng.views
WHERE query LIKE '%eng.columns%';
```

## References

- [Engine Catalog](README.md)
- [Statement Reference](../statements/README.md)
