# MONGODB
Connects to MongoDB document databases using the official MongoDB.Driver. Supports querying collections, dynamic schema discovery, nested document/array flattening to JSON strings, and database writes.

Syntax:
  CREATE CONNECTION <name> AS MONGODB(
    CONNECTION_STRING = 'mongodb://localhost:27017',
    DATABASE       = 'dbname',
    COLLECTION     = 'collection_name',
    TIMEOUT_SECONDS = 30
  );

Aliases:
  MONGO

Options:
  CONNECTION_STRING      — Connection URI (e.g., mongodb://localhost:27017 or mongodb+srv://...) (required unless host/port specified)
  DATABASE               — target database name (required)
  COLLECTION             — target collection name
  TIMEOUT_SECONDS        — connection and query timeout limit in seconds (default: 30)
  HOST                   — hostname for the MongoDB instance (alternative to CONNECTION_STRING)
  PORT                   — port for the MongoDB instance (default: 27017)
  USER                   — database username (alternative to CONNECTION_STRING authentication)
  PASSWORD               — database password (alternative to CONNECTION_STRING authentication)

### Nested Document and Array Flattening
MongoDB allows rich, hierarchical structures. When queried, this connector flattens nested BSON documents and BSON arrays to valid JSON strings in the output table, allowing standard ETL-SQL string/JSON functions to be run downstream.

### Examples

```sql
-- Connect using connection URI
CREATE CONNECTION MongoProd AS MONGODB(
  CONNECTION_STRING = 'mongodb://admin:secret@mongo.corp.local:27017',
  DATABASE   = 'analytics',
  COLLECTION = 'user_events'
);

-- Extract data into engine staging table
SELECT id, event_type, metadata, timestamp
  INTO #staging
  FROM MongoProd.user_events
  LIMIT 100;

-- Write data back to a MongoDB collection
CREATE CONNECTION MongoDest AS MONGODB(
  CONNECTION_STRING = 'mongodb://localhost:27017',
  DATABASE   = 'archive'
);

SELECT * 
  INTO MongoDest.archived_events
  FROM #staging;
```

References:
- [Data Connectors](../../../../../Docs/Reference/Data_Connectors.md)
