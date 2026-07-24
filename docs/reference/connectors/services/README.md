# SERVICES Reference

[« Back to parent](../README.md)

| Page | Description |
| :--- | :--- |
| [ACTIVE_DIRECTORY](active-directory.md) | Connects to an Active Directory or LDAP server to perform user, group, and computer lookups. Standard |
| [API](api.md) | Universal connector for web services and REST APIs. Supports `SELECT` to call endpoints and parse JSON |
| [AZURE_BLOB](azure-blob.md) | Cloud storage connector for reading and writing files in Azure Blob Storage containers. |
| [DIRECTORY](directory.md) | Treats a local or UNC filesystem folder as a data source for file-management operations (`COPY FILE`, |
| [FTP](ftp.md) | Legacy File Transfer Protocol. Supports active and passive mode depending on the server. Used with |
| [KAFKA](kafka.md) | Connects to an Apache Kafka message-broker cluster using the Confluent.Kafka driver. `SELECT` pulls |
| [MOCKDB](mockdb.md) | Built-in, zero-configuration in-memory database for script development and testing. No credentials, no |
| [ORCHESTRATOR](orchestrator.md) | Admin service connector for an ETL-SQL Orchestrator service. Does not transfer data — statements inside |
| [PORTAL](portal.md) | Admin service connector for an ETL-SQL Portal service. Does not transfer data — statements inside an |
| [S3](s3.md) | Cloud storage connector for reading and writing files in Amazon S3 or S3-compatible object storage |
| [SFTP](sftp.md) | Secure File Transfer Protocol over SSH. Supports password and key-pair authentication (mutually |
| [SHAREPOINT](sharepoint.md) | Manages files in SharePoint Document Libraries (remote file-system operations) and reads/writes |
| [SMTP](smtp.md) | Outbound-only email connector used with the `SEND EMAIL` statement and report subscription delivery. |
| [WEBHOOK](webhook.md) | Write-only sink that POSTs each inserted row as a JSON payload to an HTTP(S) webhook endpoint — Slack, |
