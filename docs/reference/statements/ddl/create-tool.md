# CREATE TOOL

Registers a custom executable tool within the session for subsequent execution via `EXECUTE TOOL`. This allows arbitrary scripts (Python, PowerShell, etc.) or binaries to participate in the data pipeline by processing JSON Lines data over standard input and output streams.

## Syntax
```sql
-- Executable Tool
CREATE TOOL PythonETL AS EXECUTABLE (
  COMMAND = 'python',
  ARGS = 'scripts/transform.py --batch-size {batchSize}',
  WORKING_DIR = './etl-scripts',
  TIMEOUT = 120
);

-- Containerized Tool (OCI Hardened)
CREATE TOOL PiiMasker AS CONTAINER (
  IMAGE = 'myregistry.azurecr.io/pii-masker:v1',
  COMMAND = 'python',
  ARGS = '/app/mask.py',
  ISOLATE_NETWORK = TRUE
);
```

## Options
- **COMMAND** — the executable to run (e.g., `python`, `pwsh`, `/bin/bash` or a direct binary path).
- **ARGS** — the arguments to pass to the executable or container. Supports parameter substitution using `{ParamName}` syntax when invoked with the `WITH` clause in `EXECUTE TOOL`.
- **WORKING_DIR** — the working directory to launch the tool from.
- **TIMEOUT** — maximum execution time in seconds before the tool is killed (default: 60).
- **IMAGE** — (CONTAINER only) the OCI image to run the tool within.
- **ISOLATE_NETWORK** — (CONTAINER only) if true, runs the container with `--network none` (default: true).
- **CAPABILITY_MOUNTS** — (CONTAINER only) comma-separated list of host-to-container volume mounts (e.g., `/host/data:/app/data:ro`). Requires elevated tenant policy grants.
- **CAPABILITY_SECRETS** — (CONTAINER only) comma-separated list of parameter names allowed to be injected securely as environment variables instead of via `ARGS`. (e.g., `CAPABILITY_SECRETS = 'API_TOKEN'`). When invoked with `WITH (API_TOKEN = 'ENC:...')`, the engine will decrypt and pass it via `-e API_TOKEN=...`.

## Examples
```sql
-- Register a transformation script
CREATE TOOL PiiMasker AS EXECUTABLE (
    COMMAND = 'python',
    ARGS = 'mask.py --batch-size {batch_size}'
);

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
