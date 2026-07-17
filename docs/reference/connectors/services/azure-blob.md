# AZURE_BLOB
Reads files stored in Azure Blob Storage. Use as a source in SELECT to list or read blobs, or as a target to write output files.

Syntax:
  CREATE CONNECTION <name> AS AZURE_BLOB(
    ACCOUNT_NAME = 'storageaccount',
    ACCOUNT_KEY  = '<key>',
    CONTAINER    = 'container-name'
  );

  -- Or with a full SAS / connection string:
  CREATE CONNECTION <name> AS AZURE_BLOB(
    CONNECTION_STRING = 'DefaultEndpointsProtocol=https;...',
    CONTAINER         = 'container-name'
  );

Options:
- **ACCOUNT_NAME** — Azure Storage account name
- **ACCOUNT_KEY** — storage account key
- **CONTAINER** — blob container name (required)
- **CONNECTION_STRING** — full connection string (alternative to ACCOUNT_NAME + ACCOUNT_KEY)
- **PREFIX** — blob name prefix filter when listing

```sql
CREATE CONNECTION ReportBlobs AS AZURE_BLOB(
  ACCOUNT_NAME = 'mystorage',
  ACCOUNT_KEY  = @blob_key,
  CONTAINER    = 'reports'
);

-- List blobs in the container
SELECT name, size, last_modified INTO #blobs FROM ReportBlobs;

-- Copy a local result to blob storage
SELECT * FROM #output INTO ReportBlobs.'exports/summary_2024.csv';
```

References:
- [Data Connectors](../../../administration/platform/README.md)
