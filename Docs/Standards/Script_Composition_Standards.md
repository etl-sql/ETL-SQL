# Script Composition Standards

This document establishes the official design patterns, file layouts, and composition standards for writing **ETL-SQL** and **Report-SQL (`.rptsql`)** scripts. Following these patterns ensures that pipelines remain modular, readable, and easy to maintain.

---

## 1. Modular Script Structure

When developing data pipelines, avoid writing single, monolithic scripts containing thousands of lines of code. Instead, partition your logic into structured child scripts coordinated by a single orchestrator.

### 1.1 The Orchestrator Pattern (`main.etlsql`)
The top-level script (usually named `main.etlsql` or `orchestrator.etlsql`) acts as the conductor. It should:
1. Declare global input/output variables.
2. Initialize environment configurations (using `_environment.etlsql`).
3. Set global connection references.
4. Invoke child stages in the order they must execute using `RUN SCRIPT`.

- **Example**:
  ```sql
  -- main.etlsql
  DECLARE @env STRING INPUT = 'DEV';
  DECLARE @loadedRows INT = 0;

  RUN SCRIPT '_environment.etlsql' WITH (@env = @env);
  
  -- Run stages sequentially
  RUN SCRIPT 'extract_customers.etlsql' WITH (@env = @env);
  RUN SCRIPT 'transform_customers.etlsql';
  RUN SCRIPT 'load_customers.etlsql' WITH (@loadedRows = @loadedRows OUTPUT);

  PRINT 'Loaded: ' + CAST(@loadedRows AS STRING) + ' rows.';
  ```

### 1.2 Environment Isolation (`_environment.etlsql`)
Shared connections and global context options should reside in a separate configuration module, typically named `_environment.etlsql`. This script is run by the main orchestrator to instantiate connections dynamically based on the target environment variable.

---

## 2. Child Script Design & Contracts

To maintain clear separation, child scripts must be written as **isolated modules with explicit contracts**.

- **Contract Comments**: Every child script must document its dependencies at the very top of the file:
  - What variables it expects (`DECLARE ... INPUT`).
  - What in-memory `#temp` tables it expects to be staged.
  - What output variables or `#temp` tables it produces.
- **Example**:
  ```sql
  -- load_customers.etlsql
  -- EXPECTS: #staging_customers (Id INT, Name STRING, Email STRING)
  -- EXPECTS: @env STRING INPUT
  -- PRODUCES: @loadedRows INT OUTPUT
  
  DECLARE @env STRING INPUT;
  DECLARE @loadedRows INT OUTPUT = 0;

  ASSERT (SELECT COUNT(*) FROM #staging_customers) > 0, 'No customers staged';

  MERGE INTO dest_db.Customers AS T
  USING #staging_customers AS S ON T.Id = S.Id
  WHEN NOT MATCHED THEN INSERT (Id, Name, Email) VALUES (S.Id, S.Name, S.Email);

  SET @loadedRows = (SELECT COUNT(*) FROM #staging_customers);
  ```

---

## 3. Flat Nesting & Recurse Limits

- **Flat Hierarchies**: Keep your script trees shallow. A parent script executing child scripts is clean and easy to trace. Avoid deep nesting chains (e.g., `orchestrator -> child1 -> child2 -> child3 -> ...`).
- **Nesting Limit**: By default, the engine blocks calls exceeding 5 levels of recursion to prevent execution loops. If a pipeline requires complex processing, represent it as sequential calls inside the top-level orchestrator instead of nested calls.

---

## 4. Separation of Layout and Code (`.rptsql`)

Report-SQL files contain both database query code and visualization layout specifications. Keep these separate to maintain readability:

- **Order of Declarations**:
  1. Data preparation, connections, and `#temp` table generation queries first.
  2. Dataset and query specifications (`CREATE DATASET`).
  3. UI visual specifications (`CREATE VISUAL`).
  4. Dashboard pages (`CREATE PAGE`).
  5. Navigation and container binds (`CREATE CONTAINER`, `CREATE NAVIGATION`) at the very end of the file.
- **Clean Query Logic**: Never declare visual dashboard elements mixed inline with raw SQL transformation queries.

---

## References

- [Language Syntax Standards](Language_Syntax_Standards.md)
- [Zero-Trust Security Guardrails](../../AGENTS.md#3-zero-trust-security-guardrails)
