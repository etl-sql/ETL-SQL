# IoT Ingestion with Regex Filtering
Filter and clean high-frequency sensor data using regular expressions before batch loading.

```sql
-- FILE_LIST: directory first, glob filter second
DECLARE @Logs LIST = FILE_LIST('C:\IoT\logs', '*.raw');

FOREACH @Log IN @Logs
BEGIN
    -- REGEXP_SUBSTR extracts a capture group match — use group index 1
    -- (REGEXP_EXTRACT is not supported; use REGEXP_SUBSTR)
    SELECT 
        CAST(REGEXP_SUBSTR(LineContent, 'ID:(\d+)', 1, 1) AS INT)       AS SensorID,
        CAST(REGEXP_SUBSTR(LineContent, 'TEMP:([\d\.]+)', 1, 1) AS FLOAT) AS Reading
    INTO #Staging
    FROM @Log.Path   -- FLATFILE connection can be created inline from a variable path
    WHERE REGEXP_LIKE(LineContent, '^\[VALID\].*TEMP:');

    INSERT INTO dw.SensorHistory SELECT * FROM #Staging;
    MOVE FILE @Log.Path TO 'C:\IoT\archive\';
END
```
