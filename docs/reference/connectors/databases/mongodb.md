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
| `PASSWORD` | Password for authentication (supports `'ENC:...'`, `'SECRET:...'`, or `'${ENV}'`) | No |
| `TIMEOUT_SECONDS` | Connection timeout limit in seconds (default: `30`) | No |

> [!NOTE]
> `CONNECTION_STRING` (or a positional URI) and structured `HOST` / `PORT` options are mutually
> exclusive: when `CONNECTION_STRING` is provided, `HOST` and `PORT` are ignored.

## Authentication

MongoDB supports standard connection string authentication and credential options:
- **Username / Password**: Supply credentials in the connection string (`mongodb://user:pass@host:port/db`) or via `USER` and `PASSWORD` options (using `'ENC:...'`, `'SECRET:...'`, or `'${ENV}'` with `Security:AllowedEnvVars`).
- **SCRAM-SHA-256 / SCRAM-SHA-1**: Default authentication mechanism supported across standalone and replica-set clusters.

## Examples

```sql
-- Connection using standard Mongo URI
CREATE CONNECTION mongo_uri AS MONGODB('mongodb://localhost:27017', DATABASE='inventory', COLLECTION='products');

-- Connection using structured host/credentials options and quoted ENC: secret
CREATE CONNECTION mongo_struct AS MONGODB(
    HOST = 'localhost',
    PORT = '27017',
    USER = 'db_admin',
    PASSWORD = 'ENC:U2FsdGVkX1+exampleCiphertext==',
    DATABASE = 'inventory',
    COLLECTION = 'products',
    TIMEOUT_SECONDS = 15
);

-- Query a collection
SELECT name, price, category FROM mongo_uri;

-- Insert a new document
INSERT INTO mongo_uri (name, price, category) VALUES ('Wireless Mouse', 29.99, 'Electronics');
```

## Write Path & Transactions

- **Bulk / write path** — `INSERT INTO` and `COPY INTO` convert each batch of rows to `BsonDocument` instances and write them with `InsertManyAsync`. `COPY INTO ... APPEND=FALSE` drops the target collection (`DropCollectionAsync`) before inserting. There is no SQL pushdown (`MongodbDataSource` implements `IDataSource`, not `IDatabaseSource`).
- **`MERGE` behavior** — Engine-emulated. `MERGE` reads the full collection and source into bounded engine memory, computes the merged result, drops the collection (`DropCollectionAsync`), and inserts the merged documents via `InsertManyAsync`. Keep `MERGE` scoped to collections that fit comfortably within `Engine:OperatorMemoryGrantMB`.
- **Transactions** — **Not supported** (`MongodbDataSource` does not implement `ITransactionalDataSource`). `BEGIN TRANSACTION` / `COMMIT` / `ROLLBACK` has no effect on MongoDB writes; each `InsertManyAsync` batch is independent.
- **`WHAT_IF` behavior** — Under `SET WHAT_IF ON`, `WriteBatches` returns without connecting or dropping/inserting documents. `MERGE` and `INSERT` log the dry-run action or row count without mutating MongoDB.
- **Idempotent-load pattern** — Stage and deduplicate in a `#temp` table first, then load into a dedicated staging collection or replace a small target collection with `COPY INTO mongo_uri FROM #clean APPEND=FALSE` after running a `SET WHAT_IF ON` dry run.

## Troubleshooting

- **Authentication Failed**: Verify database name matches the auth database (often `admin`).
- **Connection Timeout**: Ensure MongoDB port (default 27017) is accessible and cluster IP allowlist includes the ETL-SQL host.
- **BSON Type Conversion**: Complex nested documents are converted to JSON strings or flattened automatically.

## References

- [Database Connectors](README.md)
- [Connectors](../README.md)
