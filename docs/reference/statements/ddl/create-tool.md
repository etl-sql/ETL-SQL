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
# Executable tool
etl-sql admin machine tool set --name PythonETL --type EXECUTABLE \
  --option COMMAND=python \
  --option "ARGS=scripts/transform.py --batch-size {batchSize}" \
  --option TIMEOUT=120

# Containerized tool (OCI hardened; the image must be pinned by digest)
etl-sql admin machine tool set --name PiiMasker --type CONTAINER \
  --option IMAGE=myregistry.azurecr.io/pii-masker@sha256:<digest> \
  --option COMMAND=python --option ARGS=/app/mask.py --option ISOLATE_NETWORK=true

etl-sql admin machine tool list
etl-sql admin machine tool delete --name PiiMasker
```

## Options
- **COMMAND** — the executable to run (e.g., `python`, `pwsh`, `/bin/bash` or a direct binary path).
- **ARGS** — the arguments to pass to the executable or container. Supports parameter substitution using `{ParamName}` syntax when invoked with the `WITH` clause in `EXECUTE TOOL`.
- **WORKING_DIR** — the working directory to launch the tool from.
- **TIMEOUT** — maximum execution time in seconds before the tool is killed (default: 60).
- **IMAGE** — (CONTAINER only) the OCI image to run the tool within.
- **ISOLATE_NETWORK** — (CONTAINER only) if true, runs the container with `--network none` (default: true).
- **CAPABILITY_MOUNTS** — (CONTAINER only) comma-separated list of host-to-container volume mounts (e.g., `/host/data:/app/data:ro`). Mounts are trusted administrator configuration: only the machine tool catalog (`etl-sql admin machine tool set`) can set them, and their host paths are not checked against the running script's file policy.
- **CAPABILITY_SECRETS** — (CONTAINER only) comma-separated list of parameter names allowed to be injected securely as environment variables instead of via `ARGS`. (e.g., `CAPABILITY_SECRETS = 'API_TOKEN'`). When invoked with `WITH (API_TOKEN = 'ENC:...')`, the engine will decrypt and pass it via `-e API_TOKEN=...`.

## Examples
```sql
-- After: etl-sql admin machine tool set --name PiiMasker --type EXECUTABLE
--          --option COMMAND=python --option "ARGS=mask.py --batch-size {batch_size}"
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
