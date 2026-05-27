# TRANSFER Operations
SEND FILE and RECEIVE FILE move files between the local file system and a remote server connection (SFTP, FTP, or Azure Blob).

Syntax:
  SEND    FILE 'local_path'  TO 'remote_path' AT <connection>;
  RECEIVE FILE 'remote_path' TO 'local_path'  AT <connection>;

The connection must be of type SFTP, FTP, or AZURE_BLOB.

```sql
-- Download today's data file from an SFTP server
CREATE CONNECTION DataFeed AS SFTP(
  HOST    = 'sftp.supplier.com',
  USER    = @user,
  KEYFILE = 'C:\keys\supplier_rsa'
);

RECEIVE FILE 'incoming/orders_today.csv' TO 'C:\data\orders.csv' AT DataFeed;

-- Process the file
SELECT * INTO #orders FROM LocalCSV;
-- ... transform ...

-- Upload the result
SEND FILE 'C:\output\summary.csv' TO 'outgoing/summary.csv' AT DataFeed;
```

For email delivery use SEND EMAIL. For blob-based transfer use AZURE_BLOB connections with SEND FILE.

References:
- [Specialized Operations](../../../../../Docs/Reference/Specialized_Operations.md)
