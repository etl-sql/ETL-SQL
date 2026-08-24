# ETL Reference

[« Back to parent](../README.md)

| Page | Description |
| :--- | :--- |
| [Automated SFTP Bursting](automated-sftp-bursting.md) | Split a single large production table into multiple encrypted country-specific CSV files and SFTP them to separate vendor folders. |
| [Automated Slack/Teams Alerting](automated-slack-teams-alerting.md) | Centralized error reporting pattern using `SEND EMAIL` configured for webhook-style SMTP. |
| [The Batch Directory Ingester (Automation)](batch-directory-ingester.md) | Processes all new files in a directory, loads them into a central store, and moves them to an archive folder. |
| [Cross-Platform Reconciliation](cross-platform-reconciliation.md) | Compare local flat files against a remote production database to identify missing sync records. |
| [Data Quality Gate](data-quality-gate.md) | Assert data quality before loading. This pattern catches bad data (nulls, orphaned keys, out-of-range values) and either fails the job or routes ba... |
| [Dead-Letter Queue (Error Row Routing)](dead-letter-queue.md) | Instead of failing an entire load when individual rows are bad, route problem rows to a dead-letter table for later inspection and reprocessing. Go... |
| [Dynamic SQL with EXEC](dynamic-sql-with-exec.md) | Build and execute SQL statements at runtime — essential for parameterized table names, dynamic column lists, and multi-tenant pipelines where the s... |
| [End-to-End Lineage Across Two Scripts (Flat File → EDW → Report)](end-to-end-lineage.md) | A column in a report is worth little if nobody can say where its number came from. This recipe |
| [Financial Reporting (PIVOT)](financial-reporting-pivot.md) | Rotate vertical transaction logs into a horizontal quarterly summary for executive reporting. |
| [Full Refresh (Truncate & Reload)](full-refresh.md) | The simplest load strategy — wipe the target and reload completely from source. Appropriate for small reference/dimension tables where CDC is overk... |
| [Fuzzy Entity Resolution (Customer De-duplication)](fuzzy-entity-resolution.md) | Inbound names from third-party feeds rarely match your canonical list exactly. `FUZZY JOIN` matches on a similarity score instead of equality and i... |
| [Graph Ingestion & Querying (Neo4j)](graph-ingestion-neo4j.md) | This recipe demonstrates how to ingest relational nodes and edges into Neo4j, and query them back using native Cypher pushdown block syntax. |
| [Immutable Published Script Bundles (CI/CD Deployment)](immutable-published-bundles.md) | This pattern compiles and packages a multi-file script folder into an immutable versioned bundle inside the Orchestrator lockbox. It then registers... |
| [Importing Curated Lineage & Tags (Non-Standard Sources)](importing-curated-lineage.md) | When column documentation, ownership, or lineage already lives in your own catalog — a spreadsheet, a governance tool, or a previous run's OpenLine... |
| [Incremental Load with High-Water Mark](incremental-load-with-high-water-mark.md) | The most fundamental production ETL pattern. Store a watermark (the last successfully loaded timestamp or ID), extract only rows newer than it on e... |
| [IoT Ingestion with Regex Filtering](iot-regex-filtering.md) | Filter and clean high-frequency sensor data using regular expressions before batch loading. |
| [Master-Detail Cross-Report Drill-through](master-detail-drill-through.md) | The most powerful interactive pattern. It allows navigating from a high-level summary report to a completely separate, detailed report file while p... |
| [Multi-Context Join](multi-context-join.md) | Join data from three different platforms (SQL, Postgres, and CSV) in a single engine statement. |
| [The Parallel Dimension Loader](parallel-dimension-loader.md) | Optimizes runtime by loading independent, non-conflicting dimension tables simultaneously. |
| [Secure PII Masking & Hashing](pii-masking-and-hashing.md) | Anonymize sensitive customer data for compliance before moving it from PROD to a Dev/QA environment. |
| [Publishing and Operating a Portal Catalog](portal-catalog.md) | Portal administration is script-first: connect with `PORTAL`, then send catalog commands inside an `EXECUTE <portal> BEGIN ... END` block. This mak... |
| [Scheduling a Recurring Job on a Remote Orchestrator](remote-orchestrator-schedule.md) | Once a pipeline is published, register it as a scheduled job on the Orchestrator so it runs unattended with retries. Remote job creation is wrapped... |
| [REST API Ingestion](rest-api-ingestion.md) | Pull data from a REST API and load it into a database table. The `API` connector auto-handles authentication, pagination, and JSON path extraction. |
| [Outbound REST API Submission (Sink)](rest-api-submission.md) | Submit rows to a REST API destination using INSERT INTO, and capture status/response metadata in a temporary table for validation, retry, and audit... |
| [SCD Type 2 (History Tracking)](scd-type-2.md) | Tracks changes in a dimension table by expiring old records and inserting new ones with effective dating. |
| [The Secure Vendor Handshake (Export & Transmit)](secure-vendor-handshake.md) | A robust pattern for exporting sensitive internal data, securing it, and transmitting it to a vendor SFTP. |
| [Specification-Driven Vendor Feed Build](spec-driven-vendor-feed.md) | Use this pattern when a vendor gives you a PDF, Excel workbook, or data dictionary and you want a strong ETL-SQL starting point without hand-transc... |
| [The Staged Ingestion (Classical ETL)](staged-ingestion.md) | This pattern extracts data from a remote source, stages it in the Engine workspace for validation, and performs an atomic `MERGE` into the producti... |
| [Time Series Gap Filling (FILL_DATES)](time-series-gap-filling.md) | When building reporting dashboards (e.g. daily sales charts), missing dates in the raw transaction log will cause dates to be skipped on the chart ... |

---

## Related Guides

- [ETL Pipelines & Orchestration](../../guides/pipelines/README.md) — staged vs. streaming ingestion, parallel execution, DAGs, error handling, and resilience patterns
- [Data Quality & Governance](../../guides/data-quality/README.md) — column rules, quarantine remediation, cross-table assertions, and CI/CD quality gates
- [Operations & Tuning](../../guides/operations/README.md) — script logging, performance tuning, and one-person quality loops

