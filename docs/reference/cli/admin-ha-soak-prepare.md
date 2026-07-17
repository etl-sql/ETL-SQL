# etl-sql admin ha-soak prepare

Generate an isolated PostgreSQL HA soak topology run root

## Synopsis

```text
etl-sql admin ha-soak prepare [options]
```

## Options

| Option | Description |
| :--- | :--- |
| `--compose-file` | Docker Compose file used by the generated topology. |
| `--env-example` | Environment template used by the generated topology. |
| `--force, -f` | Overwrite existing generated HA soak artifacts. |
| `--image-tag` | Container image tag to use when preparing the topology. |
| `--orchestrator-port` | Host port for the HA soak Orchestrator endpoint. |
| `--orchestrator-scale` | Orchestrator replica count for the HA soak topology. |
| `--output-root` | Directory for generated HA soak runs or diagnostics. |
| `--portal-port` | Host port for the HA soak load-balanced Portal endpoint. |
| `--portal-scale` | Portal replica count for the HA soak topology. |
| `--postgres-port` | Host port for the HA soak PostgreSQL endpoint. |
| `--pull` | Pull container images before starting the generated topology. |
| `--run-id` | Stable run identifier for generated HA soak topology artifacts. |
| `--start` | Start the generated Docker topology after writing the environment files. |
| `--validate-only` | Validate the topology/script contract without writing runtime artifacts. |

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
