# MONGODB

Connects to a MongoDB document database using the official MongoDB.Driver. Querying a `MONGODB`
connection via `SELECT` retrieves documents from the specified database and collection; inserting
writes new documents. Supports dynamic schema discovery and nested document/array flattening to JSON
strings.

Aliases: `MONGO`

## Options

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `CONNECTION_STRING` | Full MongoDB connection URI (e.g. `mongodb://localhost:27017`) | Yes (if host/port not provided) |
| `DATABASE` | Target database name | Yes |
| `COLLECTION` | Target collection name context | No |
| `HOST` | MongoDB hostname (alternative to connection string) | No |
| `PORT` | MongoDB port (alternative to connection string, default: `27017`) | No |
| `USER` | Username for authentication | No |
| `PASSWORD` | Password for authentication (supports `ENC:`) | No |
| `TIMEOUT_SECONDS` | Connection timeout limit in seconds (default: `30`) | No |

## Examples

```sql
-- Connection using standard Mongo URI
CREATE CONNECTION mongo_uri AS MONGODB('mongodb://localhost:27017', DATABASE='inventory', COLLECTION='products');

-- Connection using structured host/credentials options
CREATE CONNECTION mongo_struct AS MONGODB(
    HOST = 'localhost',
    PORT = '27017',
    USER = 'db_admin',
    PASSWORD = ENC:U2FsdGVkX1+...,
    DATABASE = 'inventory',
    COLLECTION = 'products',
    TIMEOUT_SECONDS = 15
);

-- Query a collection
SELECT name, price, category FROM mongo_uri;

-- Insert a new document
INSERT INTO mongo_uri (name, price, category) VALUES ('Wireless Mouse', 29.99, 'Electronics');
```

## References

- [Database Connectors](README.md)
- [Connectors](../README.md)
