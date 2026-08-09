# The Batch Directory Ingester (Automation)
Processes all new files in a directory, loads them into a central store, and moves them to an archive folder.

**Pattern Scenario:** Process inbound daily CSV drops.

```sql
-- FILE_LIST takes a directory path and an optional filter as separate arguments
DECLARE @Drops LIST = FILE_LIST('C:\Inbound', '*.csv');

IF LENGTH(@Drops) = 0
BEGIN
    PRINT 'No files found. Exiting.';
    RETURN;
END

FOREACH @File IN @Drops
BEGIN
    BEGIN TRY
        -- 2. Bulk Load directly to Staging
        -- BULK INSERT uses FIRSTROW=2 to skip a header row, not HEADER=ON
        BULK INSERT #DailyRaw 
        FROM @File.Path 
        WITH (FORMAT='CSV', FIRSTROW=2, STRICT_SCHEMA=ON);
        
        -- 3. Archive the processed file
        DECLARE @ArchiveDir = 'C:\Archive\' + FORMAT(GETDATE(), 'yyyyMMdd');
        IF NOT DIRECTORY_EXISTS(@ArchiveDir)
        BEGIN
            CREATE DIRECTORY @ArchiveDir;
        END
        
        MOVE FILE @File.Path TO @ArchiveDir + '\' + @File.Name;
        
        PRINT 'Processed and Archived: ' + @File.Name;
    END TRY
    BEGIN CATCH
        PRINT 'Error processing ' + @File.Name + ': ' + ERROR_MESSAGE();
        -- Move to error folder instead of archive
        MOVE FILE @File.Path TO 'C:\Errors\' + @File.Name;
    END CATCH;
END;
```
