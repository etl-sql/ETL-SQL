# ETL-SQL Notebooks (.etlnb)

ETL-SQL Notebooks provide a stateful, iterative environment for writing and running ETL-SQL cells directly inside VS Code.

> **Applies to:** authoring on any profile. Notebooks are a VS Code authoring surface and need no Portal.

## Key Features

- **Cell-Based Execution**: Run small snippets of ETL-SQL and see results immediately.
- **Persistent State**: Variables (`@var`) and temporary tables (`#temp`) persist across cells within the same notebook session.
- **Interactive Mode**: Automatically enabled for notebooks, modifying engine behavior for exploration.
- **Inline Outputs**: query results render as notebook output tables; `CREATE VISUAL` statements emit their resolved visual manifest for inspection.
- **Export Path**: notebooks can be exported to a regular `.etlsql` script for scheduling, source control review, or CI.

---

## Interactive Mode Differences

When running in a notebook, the engine operates in **Interactive Mode**. This introduces several behaviors designed for rapid iteration:

### Idempotent Authoring
In standard scripts, creating a connection or dataset that already exists can fail. In Interactive Mode, authoring operations are friendlier for repeated cell execution:
- **CREATE CONNECTION**: If the connection exists, it is updated.
- **CREATE DATASET**: Re-running a cell can update the dataset definition.
- **CREATE VISUAL**: Re-running a cell replaces the visual definition with the same name.

### Immediate Output
In a standard script, `CREATE VISUAL` registers a definition for a later report page or export. In a notebook, the extension also emits the visual manifest to the cell output so you can inspect the resolved definition immediately.

### Transaction Safety
If a cell execution fails or is cancelled inside a `BEGIN TRANSACTION`, use the **ETL-SQL: Rollback All Transactions** command from the command palette to clear any dangling transaction state in the active REPL session.

---

## Example Notebook Workflow

### Cell 1: Setup Connections
```sql
CREATE CONNECTION logs AS FLATFILE('C:/Data/app_logs.csv');
DECLARE @Threshold INT = 500;
```

### Cell 2: Explore Data
```sql
SELECT TOP 100 * 
FROM logs 
WHERE Severity = 'Error';
```

### Cell 3: Create a Visual
```sql
CREATE VISUAL ErrorTrend AS LINE (
    SOURCE = (SELECT Date, COUNT(*) as ErrCount FROM logs GROUP BY Date),
    MAPPINGS (X = Date, Y = ErrCount)
);
```

---

## Commands

| Command | Description |
| :--- | :--- |
| **Run Cell** | Executes the selected cell using the ETL-SQL notebook controller and updates notebook session state. |
| **Stop Cell** | Sends a cancellation request to the active engine execution. |
| **ETL-SQL: Rollback All Transactions** | Forces a rollback of active transactions in the current REPL session. |
| **ETL-SQL: Export Notebook to .etlsql** | Writes code cells to a regular ETL-SQL script. |

---

## File Format
ETL-SQL Notebooks are saved as `.etlnb` files, which are JSON-based and compatible with VS Code's native notebook interface.
