# IoT Ingestion with Regex Filtering
<!-- description: **▶ Runs as-is.** Filter and clean high-frequency sensor data using regular expressions (`REGEXP_LIKE` and `REGEXP_SUBSTR`) before batch loading. -->
Filter and clean high-frequency sensor data using regular expressions before batch loading.

## Try It Now (No Database Needed)

Paste into a `.etlsql` file and press **F5**. An inline `#raw_logs` table stands in for a stream of raw sensor lines containing both valid readings and corrupted frames:

```sql
CREATE TABLE #raw_logs (LineContent VARCHAR(200));

INSERT INTO #raw_logs (LineContent) VALUES
    ('[VALID] 2026-04-01T10:00:01Z ID:101 TEMP:22.4'),
    ('[ERROR] 2026-04-01T10:00:02Z ID:101 CHECKSUM_FAIL'),
    ('[VALID] 2026-04-01T10:00:03Z ID:205 TEMP:19.8'),
    ('[VALID] 2026-04-01T10:00:04Z ID:101 TEMP:23.1'),
    ('[WARN]  2026-04-01T10:00:05Z ID:309 BATTERY_LOW');

-- Filter valid frames and extract typed fields with lookbehind patterns
SELECT
    CAST(REGEXP_SUBSTR(LineContent, '(?<=ID:)\d+') AS INT)           AS SensorID,
    CAST(REGEXP_SUBSTR(LineContent, '(?<=TEMP:)[\d\.]+') AS FLOAT)   AS Reading
INTO #staging
FROM #raw_logs
WHERE REGEXP_LIKE(LineContent, '^\[VALID\].*TEMP:');

SELECT SensorID, Reading
FROM #staging
ORDER BY SensorID, Reading;
```

Only the 3 `[VALID]` frames with a `TEMP:` reading pass the filter and land in `#staging` as typed `SensorID` and `Reading` columns.

## Production Version

**Pattern Scenario:** Sweep a folder of `.raw` sensor log files, filter valid lines, load into `dw.SensorHistory`, and archive each file.

```sql
-- FILE_LIST: directory first, glob filter second
DECLARE @Logs LIST = FILE_LIST('C:\IoT\logs', '*.raw');

FOREACH @Log IN @Logs
BEGIN
    -- REGEXP_SUBSTR returns the full regex match — use a lookbehind to capture only the value
    SELECT
        CAST(REGEXP_SUBSTR(LineContent, '(?<=ID:)\d+') AS INT)         AS SensorID,
        CAST(REGEXP_SUBSTR(LineContent, '(?<=TEMP:)[\d\.]+') AS FLOAT) AS Reading
    INTO #Staging
    FROM @Log.Path   -- FLATFILE connection can be created inline from a variable path
    WHERE REGEXP_LIKE(LineContent, '^\[VALID\].*TEMP:');

    INSERT INTO dw.SensorHistory SELECT * FROM #Staging;
    MOVE FILE @Log.Path TO 'C:\IoT\archive\';
END
```
