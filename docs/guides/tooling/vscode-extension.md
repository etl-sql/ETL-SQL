# VS Code Extension

ETL-SQL ships with a dedicated VS Code extension (`src/etl-sql-vscode/`) that enhances the development experience. The extension communicates with the engine via the JSON REPL protocol (`ETL-SQL ui repl`).

**Key features:**
- **Syntax highlighting** for `.etlsql` and `.rptsql` files
- **Inline LINT** — static analysis errors appear as squiggles as you type
- **Execution tree** — visual representation of the running pipeline
- **Variable sidebar** — live display of declared and runtime variable values
- **Report preview panel** — `CREATE PAGE` dashboards rendered inline for `.rptsql` files
- **Slicer interaction** — filter parameters can be changed in the sidebar without re-running the full script

**Starting the extension host:**
The extension auto-launches `ETL-SQL ui repl` in the background when you open an `.etlsql` or `.rptsql` file. For configuration, see the VS Code settings under `etlsql.*`.

---

> **Applies to:** authoring on any profile. The extension talks to the local engine and needs no Portal.

## Configuration & Deployment

Host-level settings, including security limits, dashboard ports, and background service deployment (NSSM/systemd), are now managed in the central **[Administrators Guide](../../administration/platform/README.md)**.

Refer to that guide for:
- **`appsettings.json`** configuration keys.
- **Security Limits** (runaway protection).
- **Background Service** installation.
- **Resource Governance** (memory and disk spilling).

---
