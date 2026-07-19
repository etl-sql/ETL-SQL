# SERVICES Reference

[« Back to parent](../README.md)

| Page | Description |
| :--- | :--- |
| [ACTIVE_DIRECTORY](active-directory.md) | Connects to an Active Directory or LDAP server to perform user, group, and computer lookups. Automatically translates SQL WHERE clauses into raw LD... |
| [API](api.md) | Connects to REST or HTTP endpoints. Use SELECT to call the endpoint and parse the JSON response into a result set. Use INSERT to send rows to the API. |
| [AZURE_BLOB](azure-blob.md) | Reads files stored in Azure Blob Storage. Use as a source in SELECT to list or read blobs, or as a target to write output files. |
| [DIRECTORY](directory.md) | Connects to a local or UNC file-system folder. SELECT returns a listing of files and subdirectories with their metadata. |
| [FTP](ftp.md) | Connects to an FTP or FTPS server for file transfer operations (SEND FILE, RECEIVE FILE). Not used for SELECT/INSERT — pair it with the TRANSFER op... |
| [KAFKA](kafka.md) | Connects to Apache Kafka message streams using the Confluent.Kafka driver. Supports publishing rows as JSON messages to a topic or consuming messag... |
| [MOCKDB](mockdb.md) | An in-memory test database for development and unit-testing scripts without connecting to a live database. MOCKDB accepts all DDL and DML operation... |
| [ORCHESTRATOR](orchestrator.md) | Admin service connector for remote Orchestrator job management via `EXECUTE orch BEGIN...END` (API-key auth). |
| [PORTAL](portal.md) | Admin service connector for scripted Portal administration (users, groups, folders/ACLs, reports, datasets) via `EXECUTE portal BEGIN...END`. |
| [S3](s3.md) | Connects to Amazon S3 or S3-compatible cloud object storage providers (e.g. Cloudflare R2, MinIO, Google Cloud Storage, Wasabi). Implements remote ... |
| [SFTP](sftp.md) | Connects to an SFTP (SSH File Transfer Protocol) server for secure file transfer. Use SEND FILE and RECEIVE FILE with this connection. |
| [SHAREPOINT](sharepoint.md) | Connects to a SharePoint site to perform file operations against Document Libraries and read/write queries against Lists. |
| [SMTP](smtp.md) | Connects to an SMTP mail server for sending email. Used with SEND EMAIL operations and report subscription delivery. |
