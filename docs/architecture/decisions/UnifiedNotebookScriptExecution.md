# Design Spec: Unified Notebook & Script Execution (Virtual Cells and Checkpoints)

This document specifies the design for unifying the `.etlnb` (ETL-SQL Notebook) execution controller with plain-text `.etlsql` and `.rptsql` scripts. It enables developers to execute segments of a flat script as "virtual cells" bounded by top-level labels, utilizing stateful checkpoints (`.etlsnap`) to resume execution.

---

## 1. Architectural Vision

Currently, ETL-SQL operates in two distinct modes:
1. **Script Mode (`.etlsql`)**: Evaluates a flat text file from start to finish. Useful for production runs and batch orchestration.
2. **Notebook Mode (`.etlnb`)**: Evaluates JSON-structured code cells sequentially inside a stateful REPL session. Useful for dynamic data exploration.

We are introducing the **Unified Execution Model**, which bridges these two modes:

```
                  ┌──────────────────────────────────────────┐
                  │          Plain Text Script (.etlsql)     │
                  │                                          │
                  │  DECLARE @Threshold INT = 500;           │
                  │                                          │
                  │  ingestion:   <─── Virtual Cell 1        │
                  │    SELECT * INTO #raw FROM db.orders;    │
                  │                                          │
                  │  sftp_file:   <─── Virtual Cell 2        │
                  │    SEND FILE '#raw' AT vendor_sftp;      │
                  └────────────────────┬─────────────────────┘
                                       │
                                       ▼
                       Virtual Cell Slicing (LSP)
                                       │
                                       ▼
                  Exposed to Notebook REPL Execution Controller
```

By treating top-level labels as implicit cell boundaries, a plain-text script can be executed cell-by-cell inside the notebook REPL session, complete with checkpoint serialization and environment bootstrapping.

---

## 2. Technical Mechanics

The unified execution model relies on three key mechanisms implemented in the Language Server and C# Engine:

### 2.1 Virtual Cell Slicing (LSP)
The Language Server (`ETL-SQL.LanguageServer`) parses the open `.etlsql` script and identifies all **top-level labels** (labels not nested inside `IF`, `WHILE`, `TRY...CATCH`, or `PARALLEL` blocks). 
- It segments the script into a list of virtual cells.
- Example:
  - **Virtual Cell 0 (Header)**: The top of the script down to the first top-level label. Typically holds variables and connection declarations.
  - **Virtual Cell 1**: From the first label down to the second label.
  - **Virtual Cell 2**: From the second label to the end of the script.
- The LSP server exposes these boundaries to VS Code, which renders inline `[ Run Cell ]` and `[ Run Below ]` CodeLens controls.

### 2.2 Stateless JIT Pre-Scanning (Cold Starts)
If a developer opens a script and clicks `[ Run Cell ]` on Virtual Cell 2 (e.g. `sftp_file:`), the active REPL session is empty, and the code will fail because preceding declarations (variables, connections) are missing.
To solve this, the engine performs a **Pre-scan Pass**:
1. It scans all code blocks preceding the targeted cell.
2. It executes **only** `DECLARE` and `CREATE CONNECTION` statements to establish the environment context.
3. It completely skips all heavy statements (like `SELECT INTO`, DML, and file transfers).
4. Once the target label is reached, the engine switches back to normal execution.

### 2.3 Checkpoint Bootstrapping (`.etlsnap` Restore)
When a script runs headlessly, the engine automatically writes encrypted **`.etlsnap`** packages to disk at each top-level label.
If a production job fails at a label (e.g. `sftp_file:`), the developer can import that `.etlsnap` package into their IDE/TUI:
1. The IDE loads the `.etlsnap` file.
2. It restores the variables (from JSON) and `#temp` tables (from Arrow IPC streams) directly into the active REPL session.
3. The developer can now run the failing cell immediately without executing the preceding data ingestion steps.

---

## 3. IDE User Experience (VS Code CodeLens)

When a script contains top-level labels, the editor displays inline action prompts above each label:

```sql
DECLARE @Threshold INT = 500;
CREATE CONNECTION db AS MSSQL(SERVER='localhost', DATABASE='Sales', TRUSTED_CONNECTION=TRUE);

[ Run Cell ]  [ Run Below ]
ingestion:
SELECT * INTO #raw FROM db.orders;

[ Run Cell ]  [ Run Below ]
sftp_file:
COMPRESS FILE '#raw' TO 'data/export.zip';
SEND FILE 'data/export.zip' TO '/inbox/' AT vendor_sftp;
```

- **`[ Run Cell ]`**: Sends the text of the virtual cell to the active REPL session. State persists in memory.
- **`[ Run Below ]`**: Runs the pre-scan pass (or loads a checkpoint) and executes everything from the label to the end of the script.

---

## 4. Strategy & Portability Benefits

1. **Production-to-Dev Continuity**: If an Orchestrator job fails, the developer debugs the exact state at the failure point by loading the `.etlsnap` checkpoint directly into their IDE.
2. **Simplified Development**: No need to maintain a separate `.etlnb` file for exploration and a `.etlsql` file for production. You write a single `.etlsql` script, test it interactively like a notebook, and commit it directly to source control.

---

### References
- [Session Checkpointing Grammar Reference](../../guides/getting-started.md#413-labels-and-goto)
- [Language Server Protocol Handlers](../LanguageServer.md)
- [ETL Notebook Guide](../../guides/notebook-guide.md)
