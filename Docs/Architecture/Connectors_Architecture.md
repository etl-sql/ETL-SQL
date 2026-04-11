# ETL-SQL Connectors Architecture & Engineering

This document provides a technical deep-dive into the architectural design of the ETL-SQL data access layer. It details the connection lifecycle, registry management, and the high-performance batching protocols used for cross-platform data movement.

---

## 1. Architectural Overview

The ETL-SQL Connector system is a provider-agnostic abstraction layer that sits between the **Evaluator** and external data sources. It ensures that the engine can interact with any data source using a unified relational contract.

### 1.1 Connector Topology
```text
[ Script Evaluator ]
       |
[ IConnectorRegistry ] <--- Auto-Registration (Reflection/DI)
       |
+------+-----------+----------------+
|      |           |                |
[ MSSQL ]      [ POSTGRES ]      [ FLATFILE ] <--- Connectors (Interface Level)
|      |           |                |
+------+-----------+----------------+
       |
[ IDataSource ] <--- Active Session Instance
       |
[ Data Stream (Batches) ]
```

---

## 2. Core Interfaces & Contracts

### 2.1 `IConnector` (The Factory)
The `IConnector` serves as a stateless factory and metadata provider for a specific database engine or file format.

- **`Name / Aliases`**: Unique identifiers for the `ON` clause in `CREATE CONNECTION`.
- **`GetExcludedKeywords()`**: Critically used by the **Linter** to prevent dialect-specific syntax violations (e.g., rejecting `TOP` for Postgres destinations).
- **`CreateDataSource()`**: Instantiates an `IDataSource` with a validated connection string and options dictionary.

### 2.2 `IDataSource` (The Active Stream)
Represents a stateful connection to a specific resource. It is responsible for the actual execution and data retrieval.

- **`WriteBatches()`**: The inverse of reading, used by `INSERT INTO`, `BULK INSERT`, and `MERGE`.
- **`ExecuteScalar()` / `ExecuteNonQuery()`**: Used for DDL and variable assignments.

### 2.3 `IDatabaseSource` (The Optimizer Bridge)
Specialization of `IDataSource` for SQL-capable engines (Snowflake, Databricks, BigQuery, MSSQL).
- **`SupportsSqlPushdown`**: A boolean flag that signals to the engine that the source can receive and execute raw SQL blocks.
- **`ExecuteRawSql()`**: The primary sink for optimized query blocks. This enables MPP (Massive Parallel Processing) by letting the target database handle the workload rather than the ETL-SQL engine.
- **`DialectProfile`**: Informs the engine's translation logic of the target's native syntax (e.g., paging, quoting, and temporal keywords).

---

## 3. The "Stream-to-Sink" Walkthrough: `MERGE` Lifecycle

To understand how ETL-SQL moves billions of rows without memory exhaustion, consider a `MERGE` operation between a Postgres source and an MSSQL sink.

### Phase 1: Context Resolution
1. **Evaluator** receives the `MergeStatement`.
2. It resolves names `pg` and `sql` to active `IDataSource` instances in the session state.

### Phase 2: Pipeline Initialization
1. The **Reader** (`PostgresDataSource`) prepares the source query. It returns an `IAsyncEnumerable<Batch>`.
2. The **Evaluator** creates a `BatchPipeline` connecting the source stream to the target.

### Phase 3: The Batching Loop (O(1) Memory)
1. **Pull**: The Source Reader pulls 10,000 rows into a `DataTable`.
2. **Transform**: The Engine applies any row-level logic (e.g., `UPPER(Name)`) or `CASE` expressions to the batch transit.
3. **Push**: The processed batch is sent to the **Sink Writer** (`SqlServerDataSource`).
4. **Flush**: The Sink performs a high-speed bulk copy (e.g., `SqlBulkCopy`) into a staging table or the target.
5. **Repeat**: The batch is cleared and the loop pulls the next chunk until the source is exhausted.

---

## 4. Error Propagation & Sanitization

Connectors operate at the system boundary and are prone to provider-specific errors (Network timeouts, Auth failures, Syntax errors).

- **Standardization**: All raw provider exceptions (e.g., `ORA-01017`) MUST be caught and wrapped in a `Core.ExecutionException`.
- **Sanitization**: Connectors must strip sensitive data (credentials, absolute server paths not owned by the user) from the exception message before re-throwing.

---

This metadata is utilized by the `SecurityService` and `Linter` to prevent scripts from executing if they contain keywords unsupported by the target connection. The engine's **Optimizer** further uses the `DialectProfile` to translate compatible statements into native equivalent blocks for pushdown.

---

## 6. Operational Archetypes

To maintain technical consistency, all connector development MUST follow one of two patterns:

### 6.1 The "Expansion" Archetype (Adding Options)
Used when adding production-grade features (e.g., `MIN_POOL_SIZE`, `ENCODING`) to an existing provider.
1. **Metadata**: Register the property for Linter and IDE awareness.
2. **Wiring**: Map the property to the provider's native driver configuration.
3. **Consumption**: Pass the config into the data source for execution.

### 6.2 The "Creation" Archetype (Adding New Providers)
Used when architecting a brand-new target (e.g., SAP, Salesforce).
1. **The Factory**: Implement `IConnector` metadata and `CreateDataSource` factory logic.
2. **The Wrapper**: Implement `IDataSource` (and `IDatabaseSource` if applicable) to wrap the target SDK or REST client.
3. **DI Registration**: Register the singleton in the application's Dependency Injection layer.

---
*Refer to [Connectors_Standards.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Standards/Connectors_Standards.md) for governance rules and [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) for language specs.*
