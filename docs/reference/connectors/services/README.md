# Connectors: Services

Reference pages for Services in the ETL-SQL engine.

| Name | Description |
| :--- | :--- |
| [ACTIVE_DIRECTORY](active-directory.md) | Connects to an Active Directory or LDAP server to perform user, group, and computer lookups. Automatically translates... |
| [API](api.md) | Connects to REST or HTTP endpoints. Use SELECT to call the endpoint and parse the JSON response into a result set. Us... |
| [AZURE_BLOB](azure-blob.md) | Reads files stored in Azure Blob Storage. Use as a source in SELECT to list or read blobs, or as a target to write ou... |
| [DIRECTORY](directory.md) | Connects to a local or UNC file-system folder. SELECT returns a listing of files and subdirectories with their metadata. |
| [FTP](ftp.md) | Connects to an FTP or FTPS server for file transfer operations (SEND FILE, RECEIVE FILE). Not used for SELECT/INSERT ... |
| [KAFKA](kafka.md) | Connects to Apache Kafka message streams using the Confluent.Kafka driver. Supports publishing rows as JSON messages ... |
| [MOCKDB](mockdb.md) | An in-memory test database for development and unit-testing scripts without connecting to a live database. MOCKDB acc... |
| [S3](s3.md) | Connects to Amazon S3 or S3-compatible cloud object storage providers (e.g. Cloudflare R2, MinIO, Google Cloud Storag... |
| [SFTP](sftp.md) | Connects to an SFTP (SSH File Transfer Protocol) server for secure file transfer. Use SEND FILE and RECEIVE FILE with... |
| [SHAREPOINT](sharepoint.md) | Connects to a SharePoint site to perform file operations against Document Libraries and read/write queries against Li... |
| [SMTP](smtp.md) | Connects to an SMTP mail server for sending email. Used with SEND EMAIL operations and report subscription delivery. |

## References

- [Connectors Reference](../README.md)
- [Syntax Index](../../../syntax-index.md)

