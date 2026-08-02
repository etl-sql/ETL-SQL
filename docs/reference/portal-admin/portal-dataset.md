# Portal Dataset Management
Manage named cached datasets registered in the portal inside an `EXECUTE portal` block.

## Syntax
```sql
EXECUTE portal BEGIN
  REFRESH DATASET 'DatasetName' IN FOLDER '/Folder';
  ALTER DATASET 'DatasetName' IN FOLDER '/Folder' SET (ACCESS = PUBLIC | PRIVATE, TTL = 'hh:mm:ss');
  DROP DATASET 'DatasetName' IN FOLDER '/Folder';
END;
```

## Examples
```sql
-- Mark a dataset stale so it is re-evaluated on the next refresh cycle
EXECUTE portal BEGIN
  REFRESH DATASET 'SalesSummary' IN FOLDER '/Finance';
END;

-- Make a dataset available to all portal users
EXECUTE portal BEGIN
  ALTER DATASET 'SalesSummary' IN FOLDER '/Finance' SET (ACCESS = PUBLIC);
END;

-- Restrict a dataset to users with explicit access grants and shorten the cache window
EXECUTE portal BEGIN
  ALTER DATASET 'SalesSummary' IN FOLDER '/Finance' SET (ACCESS = PRIVATE, TTL = '00:30:00');
END;

-- Remove a dataset from the portal registry
EXECUTE portal BEGIN
  DROP DATASET 'SalesSummary' IN FOLDER '/Finance';
END;

-- Inspect dataset cache health
EXECUTE portal BEGIN
  SELECT * INTO #metrics FROM eng.usage_metrics(30);
END;
SELECT dataset_name, hit_rate, last_refreshed, ttl FROM #metrics;
```

## Notes
- Portal dataset administration commands use quoted catalog names plus `IN FOLDER` to address the portal registry entry.
- Local/report dataset references use the `&Name` identifier form inside `.rptsql` scripts; do not quote those identifiers.
- `REFRESH DATASET` requires `REFRESH`, `EDITOR`, or `OWNER` dataset permission. It marks the dataset as stale and queues it for re-evaluation when portal hosting is available. Existing cached data remains available until the refresh completes.
- `TTL` (time-to-live) is specified as a duration string in `hh:mm:ss` format (e.g., `01:00:00` for one hour). After expiry, the next request triggers a refresh.
- `ACCESS = PUBLIC` requires authenticated access plus `Read` or higher on the linked portal folder. `ACCESS = PRIVATE` restricts access to the owner and groups explicitly granted dataset permission.
- Dataset permissions are hierarchical: `VIEWER`, `REFRESH`, `EDITOR`, `OWNER`. `REFRESH` permits refresh without metadata or source-query editing.
- `DROP DATASET` removes the registry entry and all cached data. Reports referencing the dataset will produce errors until they are republished with a valid dataset reference or a replacement dataset is registered.
- See: PORTAL_REPORT, PORTAL_REFRESHJOB, PORTAL_SHOW

References:
- [Data Connectors](../../administration/platform/README.md)
- [Portal Admin Commands](README.md)
