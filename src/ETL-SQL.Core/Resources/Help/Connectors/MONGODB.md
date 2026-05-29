# MONGODB
Connects to MongoDB document databases using the official MongoDB.Driver. Supports querying collections, dynamic schema discovery, nested document/array flattening to JSON strings, and database writes.

Syntax:
  CREATE CONNECTION <name> AS MONGODB(
    URI            = 'mongodb://localhost:27017',
    DATABASE       = 'dbname',
    COLLECTION     = 'collection_name',
    TIMEOUT_SECONDS = 30
  );

Aliases:
  MONGO

Options:
  URI / CONNECTION_STRING — Connection URI (e.g., mongodb://localhost:27017 or mongodb+srv://...) (required unless host/port specified)
  DATABASE / DB          — target database name (required)
  COLLECTION / TABLE     — target collection name
  TIMEOUT_SECONDS        — connection and query timeout limit in seconds (default: 30)
  HOST / SERVER          — hostname for the MongoDB instance (alternative to URI)
  PORT                   — port for the MongoDB instance (default: 27017)
  USER / UID             — database username (alternative to URI authentication)
  PASSWORD / PWD         — database password (alternative to URI authentication)

### Nested Document and Array Flattening
MongoDB allows rich, hierarchical structures. When queried, this connector flattens nested BSON documents and BSON arrays to valid JSON strings in the output table, allowing standard ETL-SQL string/JSON functions to be run downstream.

### Examples

```sql
-- Connect using connection URI
CREATE CONNECTION MongoProd AS MONGODB(
  URI        = 'mongodb://admin:secret@mongo.corp.local:27017',
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
  URI        = 'mongodb://localhost:27017',
  DATABASE   = 'archive'
);

SELECT * 
  INTO MongoDest.archived_events
  FROM #staging;
```

References:
- [Data Connectors](../../../../../Docs/Reference/Data_Connectors.md)
