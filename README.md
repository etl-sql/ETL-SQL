# 🚀 ETL-SQL Engine

![ETL-SQL Banner](https://img.shields.io/badge/ETL--SQL-v2.0-blue?style=for-the-badge&logo=dotnet)
![Language](https://img.shields.io/badge/Language-C%23-green?style=for-the-badge)
![Platform](https://img.shields.io/badge/Platform-Windows%20|%20Linux-lightgrey?style=for-the-badge)

A powerful, high-performance ETL (Extract, Transform, Load) engine that blends the simplicity of **Standard SQL** with the flexibility of **Procedural Automation**. Designed for data engineers who need to orchestrate complex data flows without leaving the comfort of a SQL-first environment.

---

## 🎨 Premium Developer Experience

### Professional Console Editor (`ui edit`)
Experience a modern, terminal-based development environment designed for productivity.

![Console Editor Mockup](file:///C:/Users/chuck/.gemini/antigravity/brain/44bd390e-4370-4efb-bd44-045ddc12d9bb/etl_sql_console_editor_mockup_1774819217352.png)

- **Vibrant Syntax Highlighting**: Context-aware coloring for DML, DDL, Control Flow, and specific ETL keywords.
- **Intelligent Autocomplete**: Deep integration with data source schemas, variables, and file systems.
- **Live Results Grid**: Interactive paging and multi-result set navigation directly in your terminal.
- **Standardized Shortcuts**:
  - `F5` / `Shift+F5`: Execute full script or current statement.
  - `Ctrl+I`: Smart Auto-Formatter.
  - `F1`: Instant Help Overlay.
  - Standard `Undo/Redo`, `Duplicate`, and `File Management` shortcuts.

### VS Code Extension Support
Leverage the power of ETL-SQL within Visual Studio Code with our dedicated language server.

![VS Code Mockup](file:///C:/Users/chuck/.gemini/antigravity/brain/44bd390e-4370-4efb-bd44-045ddc12d9bb/etl_sql_vscode_mockup_1774819235517.png)

---

## ✨ Core Pillars

### ⚡ High-Stream Performance
- **Zero-Copy Streaming**: Process billion-row datasets with a fixed memory footprint.
- **Native Pushdown**: Automatically detects when to push operations (Joins, Filters) to source databases like MSSQL or Postgres.
- **Parallel Execution**: Run data transfers and transformations in concurrent streams with the `PARALLEL` keyword.

### 🛠️ Standardized Automation Syntax
We adhere to a strict `VERB_NOUN` convention for all automation commands, ensuring a predictable and intuitive API.

| Category | Commands |
| :--- | :--- |
| **Data Flow** | `SEND_EMAIL`, `SEND_FILE`, `RECEIVE_FILE` |
| **Filesystem** | `CREATE_DIRECTORY`, `DELETE_FILE`, `COMPRESS_FILE`, `ENCRYPT_FILE` |
| **Management** | `CREATE CONNECTION`, `DROP CONNECTION`, `START_DOCKER`, `CREATE JOB` |
| **Scripting** | `CREATE PROCEDURE`, `CREATE FUNCTION`, `CREATE SETS`, `USE SETS` |
| **Analysis** | `LINT`, `EXPLAIN`, `LINEAGE()` |

### 🔍 Deep Observability
- **Data Lineage**: Trace exactly where every column came from and how it was transformed using `LINEAGE()`. Export full Mermaid.js diagrams and audit tables with `LINEAGE(#result) TO 'report.md';`.
- **Static Analysis (LINT)**: Catch logic errors, missing indices, or unoptimized joins before they hit production. Run `LINT 'script.etlsql';` to get a structured table of findings.
- **Execution Profiling**: Enable `SET PROFILING ON` to see exactly where your bottlenecks are.

---


---

## 🏗️ Technical Architecture

```mermaid
graph TD
    A[Source Script .etlsql] --> B[Lexer]
    B --> C[Parser]
    C --> D[AST Builder]
    D --> E[Evaluator]
    
    subgraph Engine Services
        E --> F[Variable Manager]
        E --> G[DataSource Manager]
        E --> H[Schema Manager]
    end
    
    G --> I[(MSSQL / Postgres)]
    G --> J[Flat Files / JSON]
    G --> K[SFTP / Azure Blob]
    G --> L[SMTP / Email]
```

---

## Executables

- **ETL-SQL.exe** — Headless Script Executor. Use in pipelines, CI/CD, cron, and server deployments. Built from `src/ETL-SQL.App/`.
- **ETL-SQL-TUI.exe** — Interactive console editor for development, debugging, and ad-hoc queries. Built from `src/ETL-SQL.TUI/` (in progress).

---

---

## 🚀 Getting Started

### Installation
Clone the repository and ensure you have the **.NET 10.0 SDK** installed.

```bash
git clone https://github.com/AmericanSuperstar/ETL-SQL.git
cd ETL-SQL
dotnet build
```

### Launch the Editor
```bash
# Open a script in the interactive editor
dotnet run --project src/ETL-SQL.App -- --ui edit MyScript.etlsql
```

### Fast-Track Example
```sql
-- Define your environment
CREATE CONNECTION prod_db ON MSSQL('Server=prod;Database=Sales;');
CREATE CONNECTION archive ON FLATFILE('C:\Exports\') WITH (DELIMITER=PIPE);
CREATE CONNECTION my_smtp ON SMTP('smtp.company.com') WITH (USERNAME='admin', PASSWORD='secret');

-- Perform the ETL
INSERT INTO archive.Sales_2026
SELECT * FROM prod_db.Orders
WHERE OrderDate >= '2026-01-01';

-- Send a notification
SEND_EMAIL TO 'admin@company.com' 
SUBJECT 'ETL Success' 
BODY 'Sales archived successfully.'
AT my_smtp;
```

---

## 📚 Documentation Library

Explore the high-fidelity documentation library for deep technical insights and practical guides.

### 📖 Getting Started & Guides
- **[User Manual](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/User_Manual.md)**: Narrative guide to the ETL-SQL "Mental Model" and first pipelines.
- **[Pattern Cookbook](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Cookbook.md)**: Ready-to-use recipes for SFTP, Incremental Loads, and Automation.

### 📜 Language Reference
- **[Language Grammar](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md)**: Total syntax reference, procedural logic, and variable management.
- **[Standard Library](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Standard_Library.md)**: Exhaustive built-in function catalog, windowing, and types.
- **[Data Connectors](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Data_Connectors.md)**: Comprehensive spec for SQL, Cloud, and FlatFile producers.

### 🏛️ Engineering & Architecture
- **[Connector Engineering](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Standards/Connectors_Engineering.md)**: Architecture deep-dive, batching protocols, and data flow.
- **[Presentation Architecture](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Architecture/Presentation_Architecture.md)**: UI/UX infrastructure and the terminal rendering engine.
- **[Security Whitepaper](file:///c:/Users/chuck/scratch/ETL-SQL/SECURITY.md)**: Zero-Trust protocols and the Script Immutability guardrail.

### 📏 Governance & Standards
- **[Connector Standards](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Standards/Connectors_Standards.md)**: Inviolable rules for data access development.
- **[Presentation Standards](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Standards/Presentation_Standards.md)**: UI consistency and error sanitization rules.
- **[AI Agent Manual](file:///c:/Users/chuck/scratch/ETL-SQL/AGENTS.md)**: Mandatory instruction set for AI-assisted development.

---

© 2026 ETL-SQL Team. Built for speed, designed for clarity.

**Commercial Use & Licensing** — This software is free for personal, non-commercial use only. If you wish to use this software for commercial purposes (including use by a business or for-profit entity), or if you are interested in a service agreement, please contact [etlsqlsoftware@gmail.com](mailto:etlsqlsoftware@gmail.com) for a commercial license.
