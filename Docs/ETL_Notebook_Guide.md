# ETL-SQL Notebooks (.etlnb)

ETL-SQL Notebooks provide a stateful, iterative environment for data engineering and visualization directly within VS Code.

## Key Features

- **Cell-Based Execution**: Run small snippets of ETL-SQL and see results immediately.
- **Persistent State**: Variables (`@var`) and temporary tables (`#temp`) persist across cells within the same notebook session.
- **Interactive Mode**: Automatically enabled for notebooks, modifying engine behavior for exploration.
- **Rich Visuals**: `CREATE VISUAL` statements render charts and tables directly in the cell output without needing a `PAGE` layout.

---

## Interactive Mode Differences

When running in a notebook, the engine operates in **Interactive Mode**. This introduces several behaviors designed for rapid iteration:

### 1. Global Idempotency
In standard scripts, declaring a variable or connection that already exists throws an error. In Interactive Mode, these operations become idempotent:
- **DECLARE**: If the variable exists, it is updated (like `SET`).
- **CREATE CONNECTION**: If the connection exists, it is patched/updated (like `ALTER`).
- **CREATE VISUAL**: Overwrites any existing visual with the same name.

### 2. Immediate Visual Emission
In a standard script, `CREATE VISUAL` only registers a definition. You must then use `CREATE PAGE` or `EXPORT` to see it.
In a notebook, `CREATE VISUAL` emits the visual manifest **immediately** to the cell output.

### 3. Transaction Safety
If a cell execution fails or is cancelled inside a `BEGIN TRANSACTION`, the transaction may remain open. 
- Use the **Rollback All Transactions** command (from the command palette or sidebar) to clear any dangling locks.

---

## Example Notebook Workflow

### Cell 1: Setup Connections
```sql
CREATE CONNECTION logs ON FLATFILE('C:/Data/app_logs.csv');
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
| **Run Cell** | Executes the current cell and updates global state. |
| **Cancel Execution** | Sends a cancellation request to the engine (interrupts long-running queries). |
| **Rollback Transactions** | Forces a rollback of all active transactions in the current engine session. |

---

## File Format
ETL-SQL Notebooks are saved as `.etlnb` files, which are JSON-based and compatible with VS Code's native notebook interface.
