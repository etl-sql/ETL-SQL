# AZURE_BLOB

Cloud storage connector for reading and writing files in Azure Blob Storage containers.

Aliases: `BLOB`

## Options

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `CONTAINER` | Target blob container name | Yes |
| `CONNECTION_STRING` | Full Azure Storage connection string | No |
| `ACCOUNT_NAME` | Azure storage account name | No |
| `ACCOUNT_KEY` | Azure storage account access key (supports `ENC:` prefix) | No |
| `SAS_TOKEN` | Shared Access Signature token (supports `ENC:` prefix) | No |
| `ENDPOINT_SUFFIX` | Custom endpoint suffix (default: `core.windows.net`) | No |
| `BLOB_ENDPOINT` | Explicit blob service endpoint URL | No |

> [!NOTE]
> Provide a full connection string in the traditional syntax, or use property-based structured syntax
> with `ACCOUNT_NAME` and `ACCOUNT_KEY` / `SAS_TOKEN`.

## Examples

```sql
-- Full connection string (SAS or AccountKey)
CREATE CONNECTION cloud AS AZURE_BLOB('DefaultEndpointsProtocol=https;AccountName=myaccount;AccountKey=abc...', CONTAINER='backup-archive');

-- Structured with account credentials
CREATE CONNECTION cloud_struct AS AZURE_BLOB(ACCOUNT_NAME='myaccount', ACCOUNT_KEY='ENC:U2FsdGVk...', CONTAINER='raw-data');
```

## References

- [Service Connectors](README.md)
- [Connectors](../README.md)
- [Amazon S3](s3.md)
