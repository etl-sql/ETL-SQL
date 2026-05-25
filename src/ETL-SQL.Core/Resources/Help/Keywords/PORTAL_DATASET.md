# Portal Dataset Management
Manage named cached datasets registered in the portal inside an `EXECUTE portal` block.

## Syntax
```sql
EXECUTE portal BEGIN
  REFRESH DATASET '&DatasetName';
  ALTER DATASET '&DatasetName' SET (ACCESS = PUBLIC | PRIVATE, TTL = 'hh:mm:ss');
  DROP DATASET '&DatasetName';
  REBUILD SNAPSHOT '&DatasetName';
END;
```

## Examples
```sql
-- Mark a dataset stale so it is re-evaluated on the next refresh cycle
EXECUTE portal BEGIN
  REFRESH DATASET '&SalesSummary';
END;

-- Force an immediate data refresh regardless of TTL
EXECUTE portal BEGIN
  REBUILD SNAPSHOT '&SalesSummary';
END;

-- Make a dataset available to all portal users
EXECUTE portal BEGIN
  ALTER DATASET '&SalesSummary' SET (ACCESS = PUBLIC);
END;

-- Restrict a dataset to users with explicit access grants and shorten the cache window
EXECUTE portal BEGIN
  ALTER DATASET '&SalesSummary' SET (ACCESS = PRIVATE, TTL = '00:30:00');
END;

-- Remove a dataset from the portal registry
EXECUTE portal BEGIN
  DROP DATASET '&SalesSummary';
END;

-- Inspect dataset cache health
EXECUTE portal BEGIN
  SHOW PORTAL USAGE METRICS INTO #metrics;
END;
SELECT dataset_name, hit_rate, last_refreshed, ttl FROM #metrics;
```

## Notes
- Portal datasets (`&Name`) are named result sets cached in the portal and shared across reports that reference them.
- Dataset names always begin with `&` — the ampersand prefix is part of the identifier and must be included in all commands.
- `REFRESH DATASET` marks the dataset as stale and queues it for re-evaluation during the next scheduled refresh cycle. Existing cached data remains available until the refresh completes.
- `REBUILD SNAPSHOT` forces an immediate, synchronous data refresh, bypassing the TTL. Use this when data must be current before a scheduled event or delivery.
- `TTL` (time-to-live) is specified as a duration string in `hh:mm:ss` format (e.g., `01:00:00` for one hour). After expiry, the next request triggers a refresh.
- `ACCESS = PUBLIC` makes the dataset visible to all authenticated portal users. `ACCESS = PRIVATE` restricts access to users or groups that have been explicitly granted permissions on the dataset's source folder.
- `DROP DATASET` removes the registry entry and all cached data. Reports referencing the dataset will produce errors until they are republished with a valid dataset reference or a replacement dataset is registered.
- See: PORTAL_REPORT, PORTAL_REFRESHJOB, PORTAL_SHOW

References:
- [Data Connectors](../../../../../Docs/Reference/Data_Connectors.md)
- [Grammar](../../../../../Docs/Reference/Grammar.md)
