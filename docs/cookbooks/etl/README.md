# ETL Recipes

Self-contained, high-fidelity recipes for real-world ETL tasks, covering the full lifecycle of data
movement from inception to archival. Every recipe is runnable as-is with correctly provisioned
connections.

## Ingestion

- [The Staged Ingestion (Classical ETL)](staged-ingestion.md) — extract from a remote source, stage in the engine workspace for validation, then `MERGE` atomically into production.
- [The Batch Directory Ingester (Automation)](batch-directory-ingester.md) — process every new file in a directory, load them into a central store, and archive them.
- [REST API Ingestion](rest-api-ingestion.md) — pull from a REST API into a database table, with authentication, pagination, and JSON flattening handled by the connector.
- [IoT Ingestion with Regex Filtering](iot-regex-filtering.md) — filter and clean high-frequency sensor data with regular expressions before batch loading.
- [Graph Ingestion & Querying (Neo4j)](graph-ingestion-neo4j.md) — ingest relational nodes and edges into Neo4j and query them back with native Cypher.
- [Specification-Driven Vendor Feed Build](spec-driven-vendor-feed.md) — turn a vendor PDF, workbook, or data dictionary into a strong ETL-SQL starting point.

## Load strategies

- [Incremental Load with High-Water Mark](incremental-load-with-high-water-mark.md) — the most fundamental production pattern: store a watermark and load only what is new.
- [Full Refresh (Truncate & Reload)](full-refresh.md) — wipe the target and reload completely. Appropriate for small reference data.
- [SCD Type 2 (History Tracking)](scd-type-2.md) — track dimension changes by expiring old records and inserting new ones with effective dating.
- [The Parallel Dimension Loader](parallel-dimension-loader.md) — load independent, non-conflicting dimension tables simultaneously.
- [Time Series Gap Filling (FILL_DATES)](time-series-gap-filling.md) — materialize missing dates per group so a chart axis has no holes.

## Transformation and matching

- [Multi-Context Join](multi-context-join.md) — join three different platforms (SQL Server, Postgres, and a CSV) in one engine statement.
- [Fuzzy Entity Resolution (Customer De-duplication)](fuzzy-entity-resolution.md) — resolve messy third-party names onto a canonical list by similarity score, routing the rest to review.
- [Financial Reporting (PIVOT)](financial-reporting-pivot.md) — rotate vertical transaction logs into a horizontal quarterly summary.
- [Dynamic SQL with EXEC](dynamic-sql-with-exec.md) — build and execute SQL at runtime for parameterized table names and dynamic column lists.
- [Cross-Platform Reconciliation](cross-platform-reconciliation.md) — compare local flat files against a remote production database to find missing sync records.

## Quality and failure handling

- [Data Quality Gate](data-quality-gate.md) — assert quality before loading, so nulls, orphaned keys, and out-of-range values stop the pipeline rather than reaching the target.
- [Dead-Letter Queue (Error Row Routing)](dead-letter-queue.md) — route problem rows to a dead-letter table instead of failing the whole load.

## Governance and lineage

- [End-to-End Lineage Across Two Scripts (Flat File → EDW → Report)](end-to-end-lineage.md) — trace a report column back to the CSV it came from, across a session boundary, transformations included.
- [Importing Curated Lineage & Tags (Non-Standard Sources)](importing-curated-lineage.md) — project documentation, ownership, or lineage that already lives in your own catalog into the engine.
- [Secure PII Masking & Hashing](pii-masking-and-hashing.md) — anonymize sensitive customer data for compliance before moving it from production to a lower environment.

## Delivery and operations

- [The Secure Vendor Handshake (Export & Transmit)](secure-vendor-handshake.md) — export sensitive data, secure it, and transmit it to a vendor SFTP.
- [Automated SFTP Bursting](automated-sftp-bursting.md) — split a large table into encrypted country-specific files and send each to its own destination.
- [Outbound REST API Submission (Sink)](rest-api-submission.md) — submit rows to a REST destination with `INSERT INTO`, capturing status and response metadata.
- [Automated Slack/Teams Alerting](automated-slack-teams-alerting.md) — centralized error reporting over webhook-style SMTP.
- [Scheduling a Recurring Job on a Remote Orchestrator](remote-orchestrator-schedule.md) — register a published pipeline so it runs unattended with retries and history.
- [Immutable Published Script Bundles (CI/CD Deployment)](immutable-published-bundles.md) — compile a multi-file script folder into an immutable versioned bundle.
- [Publishing and Operating a Portal Catalog](portal-catalog.md) — script-first Portal administration through an `EXECUTE <portal> BEGIN ... END` block.
- [Master-Detail Cross-Report Drill-through](master-detail-drill-through.md) — navigate from a high-level summary report into a separate detail report.

---

*Refer to [Standard Library](../../reference/functions/README.md) for function signatures, [Data Connectors](../../reference/connectors/README.md) for connector options, and [Getting Started](../../guides/onboarding/getting-started.md) for the mental model.*
