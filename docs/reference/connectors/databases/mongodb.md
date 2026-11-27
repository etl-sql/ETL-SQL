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
| `KEY_COLUMNS` | Comma-separated key column names that switch `INSERT`/`COPY INTO` to idempotent `BulkWriteAsync` upsert (`ReplaceOneModel(IsUpsert = true)`) | No |
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

-- Idempotent upsert by key column(s)
CREATE CONNECTION mongo_upsert AS MONGODB('mongodb://localhost:27017', DATABASE='inventory', COLLECTION='products', KEY_COLUMNS='sku');
INSERT INTO mongo_upsert SELECT sku, name, price FROM #products;
```

## Pass-Through Queries (`EXECUTE ... BEGIN ... END`)

`MONGODB` does not push down SQL (`SupportsSqlPushdown` is `false`). To filter or aggregate inside MongoDB without scanning an entire collection into engine memory, run a read-only JSON command with `EXECUTE ... BEGIN ... END`:

- **Find mode**: Specify `collection` and `filter` (JSON object), with optional `projection`, `sort`, `limit`, and `skip`.
- **Aggregate mode**: Specify `collection` and `pipeline` (JSON array of aggregation stages).
- **Read-only enforcement**: Write commands (`insert`, `update`, `delete`, `drop`, `bulkWrite`, etc.) and write pipeline stages (`$out`, `$merge`, including inside nested stages) are refused.
- **No server-side JavaScript**: `$where`, `$function`, and `$accumulator` are refused anywhere in the command.
- **Result shape**: Results come back as one table whose columns are every field seen in any document; a document without a field gets `NULL`. The whole result is held in memory, so bound large reads with `limit` or `$match`/`$project`/`$limit` stages.
- **SQL statements**: `CREATE TABLE`, `DROP TABLE`, `ALTER TABLE`, and other SQL sent to a `MONGODB` connection fail with a message pointing to this JSON form.
- **`SET WHAT_IF ON`**: Logs the command without running it against MongoDB.
- **Invalid JSON**: Fails with the line and column position without echoing the raw command body.

```sql
-- Server-side filtered find into a #temp table
EXECUTE mongo_uri INTO #electronics BEGIN
  {
    "collection": "products",
    "filter": { "category": "Electronics", "price": { "$gte": 25 } },
    "projection": { "_id": 0, "sku": 1, "name": 1, "price": 1 },
    "sort": { "price": -1 },
    "limit": 100
  }
END;

-- Server-side aggregation pipeline
EXECUTE mongo_uri INTO #category_summary BEGIN
  {
    "collection": "products",
    "pipeline": [
      { "$match": { "category": "Electronics" } },
      { "$group": { "_id": "$category", "avg_price": { "$avg": "$price" }, "count": { "$sum": 1 } } }
    ]
  }
END;
```

## Write Path & Transactions

- **Bulk / write path** — Without `KEY_COLUMNS`, `INSERT INTO` and `COPY INTO` convert each batch of rows to `BsonDocument` instances and write them with `InsertManyAsync`. With `KEY_COLUMNS` set on the connection, each batch runs `BulkWriteAsync` with `ReplaceOneModel(IsUpsert = true)` filtered on the configured key columns; any row with a `NULL` key fails the batch with the 1-based row ordinal before writing. `COPY INTO ... APPEND=FALSE` drops the target collection (`DropCollectionAsync`) first in both modes.
- **`MERGE` behavior** — Engine-emulated. `MERGE` reads the full collection and source into bounded engine memory, computes the merged result, drops the collection (`DropCollectionAsync`), and writes the merged documents. Keep `MERGE` scoped to collections that fit comfortably within `Engine:OperatorMemoryGrantMB`.
- **Transactions** — **Not supported** (`MongodbDataSource` does not implement `ITransactionalDataSource`). `BEGIN TRANSACTION` / `COMMIT` / `ROLLBACK` has no effect on MongoDB writes; each `InsertManyAsync` / `BulkWriteAsync` batch is independent.
- **`WHAT_IF` behavior** — Under `SET WHAT_IF ON`, `WriteBatches` returns without connecting or dropping/inserting/upserting documents. `MERGE` and `INSERT` log the dry-run action or row count without mutating MongoDB.
- **Idempotent-load pattern** — Set `KEY_COLUMNS` on the connection (e.g., `KEY_COLUMNS='sku'` or composite `KEY_COLUMNS='tenant_id, order_id'`) and load with `INSERT INTO` or `COPY INTO ... APPEND=TRUE` so re-running the same load replaces matching documents in place without duplicates.

## Troubleshooting

- **Authentication Failed**: Verify database name matches the auth database (often `admin`).
- **Connection Timeout**: Ensure MongoDB port (default 27017) is accessible and cluster IP allowlist includes the ETL-SQL host.
- **BSON Type Conversion**: Complex nested documents are converted to JSON strings or flattened automatically.

## References

- [Database Connectors](README.md)
- [Connectors](../README.md)
