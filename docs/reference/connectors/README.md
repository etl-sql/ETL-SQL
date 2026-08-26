# Connectors

Connections link ETL-SQL to external data sources — databases, files, APIs, and protocols.

Connector reference pages define `CREATE CONNECTION` syntax, options, authentication patterns, security behavior, and examples.

## Connector Families

- [Database connectors](databases/README.md) - MSSQL, Postgres, Oracle, MySQL, SQLite, ODBC, Snowflake, BigQuery, MongoDB, and Neo4j.
- [File connectors](files/README.md) - flat files, Excel, JSON, XML, Parquet, and Avro.
- [Service connectors](services/README.md) - API/REST, SFTP, FTP, Azure Blob, S3, SharePoint, SMTP, Webhook, Kafka, Directory, Active Directory, MockDB, Portal, and Orchestrator.

## Page Standard

Every connector page should include syntax, required options, authentication patterns, mutually exclusive options, security notes, examples, troubleshooting, and references.
For interactive code-first connection authoring, see the [Connection Wizard Guide](../../guides/feature-guides/connection-wizard.md).

Use [Connector Reference Template](../../templates/connector-reference-template.md) for new connector pages.

