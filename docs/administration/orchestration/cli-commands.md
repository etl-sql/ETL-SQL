# CLI Command Reference

## 2. CLI Command Reference

```
ETL-SQL [command] [arguments] [options]
```

Running `ETL-SQL` with no arguments (or `--help`) displays the command table.

### 2.1 `run` — Execute a Script

Runs an `.etlsql` script file and exits.

```
ETL-SQL run <script> [options]
```

**Arguments:**

| Argument | Description |
|----------|-------------|
| `<script>` | Path to the `.etlsql` script file to execute |

**Options:**

| Option | Short | Default | Description |
|--------|-------|---------|-------------|
| `--batch-size` | `-b` | `10000` | Number of rows per in-memory processing batch |
| `--perf` | `-p` | off | Print performance metrics (Lexer/Parser/Execution ms, RAM, rows/s) after execution |
| `--verbose` | `-v` | off | Print detailed statement-level execution tracking |
| `--log [path]` | `-l` | off | Enable log file. Defaults to `logs/scripts/`. Override with a path. |
| `--silent` | `-s` | off | Suppress all console output |
| `--preview [n]` | `-pr` | off | Preview top N rows of the result set in the console (`*` for all) |
| `--json` | | off | Emit all output as structured JSON (used by the VS Code extension) |
| `--page` | `-pa` | off | Pause between multiple result sets (interactive pager) |
| `--session <id>` | | none | Enable session persistence. Connections and variables survive between runs. |
| `--var @Name=Value` | `-d` | none | Inject a variable into the script. Repeatable. |
| `--progress` | `-g` | off | Display a live graphical execution tree in the console |

**Examples:**

```bash
# Simplest run
ETL-SQL run nightly_load.etlsql

# With perf metrics and logging
ETL-SQL run nightly_load.etlsql --perf --log C:\Logs\etlsql\

# Inject runtime parameters
ETL-SQL run monthly_report.etlsql --var @env=PROD --var @month=2026-03

# Headless with JSON output for automation
ETL-SQL run nightly_load.etlsql --json --silent

# Persistent session — connections survive between runs
ETL-SQL run setup_connections.etlsql --session prod-session
ETL-SQL run nightly_load.etlsql --session prod-session

# Live progress tree in the terminal
ETL-SQL run heavy_transform.etlsql --progress --perf
```

### 2.2 `ui edit` — Open the Terminal IDE

Opens the full Terminal IDE (windowed TUI editor) with optional pre-loaded file.

```
ETL-SQL ui edit [file] [options]
```

| Option | Short | Default | Description |
|--------|-------|---------|-------------|
| `[file]` | | none | Optional `.etlsql` file to pre-load into the editor |
| `--batch-size` | `-b` | `10000` | Batch size for executions started from the IDE |
| `--verbose` | `-v` | off | Verbose mode for executions started from the IDE |
| `--session <id>` | | none | Session ID — connections persist across F5 runs |

```bash
# Open the IDE with a file pre-loaded
ETL-SQL ui edit nightly_load.etlsql

# Open the IDE with a persistent session
ETL-SQL ui edit --session dev-workspace
```

### 2.3 `ui repl` — JSON REPL Protocol

Starts the JSON-based REPL protocol used by the VS Code extension. Not intended for direct interactive use.

```
ETL-SQL ui repl [options]
```

### 2.4 `encrypt` — Encrypt a Connection String

Encrypts a plaintext value (typically a connection string or password) so it can be stored safely in a script using the `ENC:` prefix.

```
ETL-SQL encrypt <value> --pass <master-password>
```

**Arguments:**

| Argument | Description |
|----------|-------------|
| `<value>` | The plaintext connection string or password to encrypt |

**Options:**

| Option | Description |
|--------|-------------|
| `--pass <password>` | The master password used for AES-256 encryption |

**Example:**

```bash
# Encrypt a connection string
ETL-SQL encrypt "Server=prod-sql;Database=DW;User Id=sa;Password=S3cr3t!" --pass MyMasterKey

# Output:
# Encrypted: ENC:U2FsdGVkX1+...

# Use in a script:
# CREATE CONNECTION prod AS MSSQL('ENC:U2FsdGVkX1+...', TRUSTED_CONNECTION=FALSE);
```

> [!IMPORTANT]
> The master password must be the same each time you run scripts referencing `ENC:` strings. Pass it at runtime with `--pass MyMasterKey` or set `USE PASSWORD = '...';` at the top of your script.

### 2.5 `session clear` — Clear Session State

Removes persisted session state (connections, variables) for the given session ID.

```
ETL-SQL session clear <id>
```

```bash
ETL-SQL session clear dev-workspace
```

### 2.6 `generate` — Generate Mock Data

Generates a large test dataset for performance validation.

```
ETL-SQL generate [--estimate <rows>]
```

### 2.7 `gen-script` — Compile Spec JSON to Script

Compiles an intermediate JSON specification contract into a validated `.etlsql` starter script. This is intended to save setup time after an LLM or developer extracts a vendor data specification into JSON; it does not replace human review or the source extraction query.

```
ETL-SQL gen-script --schema <path-to-json> --output <path-to-etlsql>
```

**Options:**

| Option | Short | Description |
|--------|-------|-------------|
| `--schema` | `-s` | Path to the input JSON schema specification file |
| `--output` | `-o` | Destination path for the compiled ETL-SQL script |

**Example:**
```bash
ETL-SQL gen-script --schema ./specs/customer_feed.json --output ./scripts/load_customers.etlsql
```

Generated scripts include schema gates, casting, lineage tags, AI review/evidence comments when present, validation issue summaries, and optional quarantine scaffolding. Review the JSON, complete the generated `#staging` extraction block, and test with real vendor files before production use. See [Spec-Driven Development](../../guides/spec-driven-development.md) and Cookbook recipe 25 for the full workflow.

### 2.8 `extract-spec` — Trim Schema Pages from Large PDF

Uses heuristic analysis to extract likely data dictionary / schema pages from large vendor PDF specifications, removing administrative fluff before LLM review.

```
ETL-SQL extract-spec --input <path-to-large-pdf> --output <path-to-trimmed-pdf>
```

**Options:**

| Option | Short | Description |
|--------|-------|-------------|
| `--input` | `-i` | Path to the input large PDF specification file |
| `--output` | `-o` | Destination path for the extracted trimmed PDF file |

**Example:**
```bash
ETL-SQL extract-spec --input ./specs/vendor_api_spec.pdf --output ./specs/trimmed_schema_spec.pdf
```

### 2.9 Exit Codes

| Code | Meaning |
|------|---------|
| `0` | Script completed successfully |
| `1` | Parse error, lint error, or runtime exception |

Exit codes are suitable for use in CI/CD pipeline gating.

---
