# RECEIVE FILE

Downloads a remote file from an `SFTP`, `FTP`, or `AZURE_BLOB` connection into local engine staging storage for processing and ingestion.

---

## Syntax

### 1. Statement Form (Recommended)
```sql
RECEIVE FILE FROM '<remote_path>' TO '<local_path>' AT <connection_name> [WITH (OVERWRITE = TRUE | FALSE)];
```

### 2. Function Shorthand
```sql
RECEIVE FILE('<remote_path>', <connection_name>, '<local_path>' [, <overwrite_boolean>]);
```

---

## Parameters & Options

- **`remote_path`** — File path on the remote host/server.
- **`local_path`** — Local destination file path on disk where the file will be saved.
- **`connection_name`** — Active `SFTP`, `FTP`, or `AZURE_BLOB` connection identifier.
- **`OVERWRITE = TRUE | FALSE`** — When `TRUE`, overwrites existing local files. When `FALSE`, throws an exception if the local target file already exists (default: `FALSE`).

---

## Examples

### 1. Basic SFTP Download

```sql
CREATE CONNECTION sftp_partner AS SFTP(
    HOST = 'sftp.vendor.com',
    USER = 'client_feed',
    PASSWORD = 'SECRET:VendorSftpPassword'
);

RECEIVE FILE FROM '/outbound/daily_catalog.csv' 
TO 'C:\staging\daily_catalog.csv' 
AT sftp_partner 
WITH (OVERWRITE = TRUE);

PRINT 'Download complete. File staged locally.';
```

### 2. Production ETL: Vendor Feed Ingestion, Decryption & Database Staging

Download a PGP-encrypted inventory feed from a third-party supplier, decrypt the contents locally, validate data quality, and merge into warehouse inventory tables:

```sql
CREATE CONNECTION supplier_sftp AS SFTP(HOST='feed.supplier.com', USER='etl_bot', KEYFILE='certs/supplier_rsa.key');
CREATE CONNECTION warehouse_db  AS MSSQL(SERVER='dw.internal', DATABASE='operations');

DECLARE @remote_file VARCHAR = '/export/inventory_feed.csv.pgp';
DECLARE @encrypted_local VARCHAR = 'C:\staging\vendor_inventory.csv.pgp';
DECLARE @decrypted_local VARCHAR = 'C:\staging\vendor_inventory.csv';

BEGIN TRY
  -- 1. Download encrypted payload from supplier SFTP
  RECEIVE FILE FROM @remote_file 
  TO @encrypted_local 
  AT supplier_sftp 
  WITH (OVERWRITE = TRUE);

  -- 2. Decrypt PGP payload
  DECRYPT FILE @encrypted_local 
  TO @decrypted_local 
  KEYFILE 'certs/private_key.asc' 
  PASSWORD 'SECRET:PgpPrivateKeyPassphrase';

  -- 3. Read and stage CSV contents in memory
  CREATE CONNECTION feed_csv AS FLATFILE(PATH=@decrypted_local);
  SELECT sku, product_name, stock_count, unit_cost 
  INTO #staged_inventory 
  FROM feed_csv.data;

  -- 4. Upsert into core warehouse
  MERGE INTO warehouse_db.dbo.DimInventory AS T
  USING #staged_inventory AS S ON T.Sku = S.sku
  WHEN MATCHED THEN
    UPDATE SET T.StockCount = S.stock_count, T.UnitCost = S.unit_cost, T.LastUpdated = GETDATE()
  WHEN NOT MATCHED THEN
    INSERT (Sku, ProductName, StockCount, UnitCost, LastUpdated)
    VALUES (S.sku, S.product_name, S.stock_count, S.unit_cost, GETDATE());

  PRINT 'Successfully ingested and merged ' + CAST(@@ROWCOUNT AS VARCHAR) + ' vendor inventory rows.';

  -- 5. Cleanup temporary local files
  DELETE FILE @encrypted_local;
  DELETE FILE @decrypted_local;
END TRY
BEGIN CATCH
  PRINT 'Vendor ingestion pipeline failed: ' + ERROR_MESSAGE();
  THROW;
END CATCH;
```

---

## References & Related Recipes

- [File Operations Reference](README.md)
- [SEND FILE](send-file.md)
- [DECRYPT FILE](encrypt-file.md)
- [SFTP Connector](../connectors/services/sftp.md)
- [ETL Cookbook: Spec-Driven Vendor Feed](../../cookbooks/etl/spec-driven-vendor-feed.md)
- [ETL Cookbook: Secure Vendor Handshake](../../cookbooks/etl/secure-vendor-handshake.md)
- [Syntax Index](../../syntax-index.md)
