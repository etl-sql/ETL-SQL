# CREATE TOOL

Tools let scripts (Python, PowerShell, etc.) or binaries take part in a pipeline by reading and writing JSON Lines over standard input and output. A script runs a tool with `EXECUTE TOOL`.

> [!IMPORTANT]
> A script cannot register a tool. `CREATE TOOL` and `DROP TOOL` parse but fail with an error: tools are trusted administrator configuration, stored in the machine tool catalog by the `etl-sql admin machine tool` commands. The tool catalog must be configured (`Governance:Tools:Provider = Local` and `Governance:Tools:LocalRoot`).

## Syntax
```sql
-- Parses, then fails with an error. Register tools with `etl-sql admin machine tool` (below).
CREATE TOOL tool_name AS { EXECUTABLE | CONTAINER } ( option = value [, ...] );
DROP TOOL tool_name;
```

## Registering a tool
```text
# Executable tool (absolute COMMAND and administrator-owned WORKING_DIR)
etl-sql admin machine tool set --name PythonETL --type EXECUTABLE \
  --option COMMAND=/usr/bin/python3 \
  --option WORKING_DIR=/opt/etlsql/tools/python-etl \
  --option SCRIPT_PATH=scripts/transform.py \
  --option "ARGS=\"scripts/transform.py\" --batch-size {batchSize}" \
  --option TIMEOUT=120

# Containerized tool (OCI hardened; the image must be pinned by digest)
etl-sql admin machine tool set --name PiiMasker --type CONTAINER \
  --option IMAGE=myregistry.azurecr.io/pii-masker@sha256:<digest> \
  --option COMMAND=python --option ARGS=/app/mask.py --option ISOLATE_NETWORK=true

etl-sql admin machine tool list
etl-sql admin machine tool delete --name PiiMasker
```

## Options
- **COMMAND** — the executable to run. For `EXECUTABLE` tools, this must be a fully qualified absolute path to an approved binary (relative paths, `PATH` lookups, UNC paths, and symlinked paths are rejected). For `CONTAINER` tools, this is the entrypoint command inside the image.
- **ARGS** — the argument template passed to the executable or container. Supports `{ParamName}` substitution from `EXECUTE TOOL ... WITH (...)` and single/double quotes for arguments or paths containing spaces (for example, `"\"C:\Program Files\ETL Tools\mask.py\" --batch-size {batch_size}"`). Template tokenization runs before placeholder substitution so caller values cannot split arguments or inject switches.
- **WORKING_DIR** — for `EXECUTABLE` tools, an optional fully qualified, administrator-owned directory (no symlinks/reparse points or UNC paths) used as the child process working directory. It is never deleted on teardown. When omitted, the child launches in its disposable per-invocation scratch directory. For `CONTAINER` tools, sets the container working directory (`-w`).
- **SCRIPT_PATH** — optional approved script path validated at registration and launch. May be a fully qualified path or a path relative to `WORKING_DIR` (relative paths require `WORKING_DIR` and cannot escape it via `..` or links).
- **TIMEOUT** — maximum execution time in seconds before the tool is killed (default: 60).
- **IMAGE** — (CONTAINER only) the OCI image to run the tool within (must be digest-pinned).
- **ISOLATE_NETWORK** — (CONTAINER only) if true, runs the container with `--network none` (default: true).
- **CAPABILITY_MOUNTS** — (CONTAINER only) comma-separated list of host-to-container volume mounts (e.g., `/host/data:/app/data:ro`). Mounts are trusted administrator configuration: only the machine tool catalog (`etl-sql admin machine tool set`) can set them, and their host paths are not checked against the running script's file policy.
- **CAPABILITY_SECRETS** — (CONTAINER only) comma-separated list of parameter names allowed to be injected securely as environment variables instead of via `ARGS`. (e.g., `CAPABILITY_SECRETS = 'API_TOKEN'`). When invoked with `WITH (API_TOKEN = 'ENC:...')`, the engine will decrypt and pass it via `-e API_TOKEN=...`.

## Scratch Directory Separation
Every `EXECUTABLE` invocation receives a fresh, disposable per-run scratch directory exposed via `ETLSQL_SCRATCH_DIR` (as well as `TMP`, `TEMP`, and `TMPDIR`). On completion, timeout, or cancellation, only that generated scratch directory is deleted; a configured `WORKING_DIR` and approved assets inside it are never removed.

## Examples
```sql
-- After: etl-sql admin machine tool set --name PiiMasker --type EXECUTABLE
--          --option COMMAND=/usr/bin/python3
--          --option WORKING_DIR=/opt/etlsql/tools/pii-masker
--          --option SCRIPT_PATH=mask.py
--          --option "ARGS=mask.py --batch-size {batch_size}"
-- Execute the tool on a data stream
EXECUTE TOOL 'PiiMasker'
FROM #raw_data
INTO #masked_data
WITH (batch_size = 500)
EXPECT SCHEMA (id INT, email STRING);
```

## References
- [Script Composition Standards](../../../architecture/standards/script-composition-standards.md)
- [Statement Reference](../README.md)
