# etl-sql run

Execute an ETL-SQL script

## Synopsis

```text
etl-sql run <script> [options]
```

## Arguments

| Argument | Required | Description |
| :--- | :--- | :--- |
| `script` | yes | The ETL-SQL script to execute. |

## Options

| Option | Description |
| :--- | :--- |
| `--batch-size, -b` | The size of data chunks to process in memory. |
| `--json` | Output results and messages in structured JSON format. |
| `--log, -l` | Enable logging. Optional: specify path/directory. |
| `--page, -pa` | Pause and page between multiple result sets in the console. |
| `--perf, -p` | Display performance metrics after execution. |
| `--preview, -pr` | Preview top N results (e.g. 20, 100, *) |
| `--progress, -g` | Display real-time graphical execution progress. |
| `--resume` | Resume execution of a persistent session from the last successfully completed checkpoint. |
| `--session` | Enable session persistence with the specified session ID. |
| `--silent, -s` | Remove all console messages. |
| `--var, -d` | Inject a variable into the script (e.g. @Name=Value). |
| `--verbose, -v` | Print detailed execution tracking. |

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
