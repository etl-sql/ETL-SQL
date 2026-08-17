# etl-sql admin promotion saas-fleet-plan

Plan a release rollout across the Managed Dedicated fleet (plans only; never upgrades)

## Synopsis

```text
etl-sql admin promotion saas-fleet-plan [options]
```

## Options

| Option | Description |
| :--- | :--- |
| `--authorization` | Change record or rollout ticket this enumeration hangs off. |
| `--execute` | Walk the planned waves, cutting over every deployment the loaded signed authorization names. |
| `--fleet-root` | Root the deployments were onboarded under; each tenant occupies its own directory. |
| `--max-failures` | Failed cutovers tolerated before the rollout stops opening waves. |
| `--operator` | Platform person or service enumerating the fleet. Never a tenant user. |
| `--reason` | Why the fleet is being enumerated, so the access can be reviewed later. |
| `--target-release` | Release every eligible deployment is being rolled to. |
| `--wave-size` | Deployments per rollout wave, so a canary wave can be small. |

## References

- [CLI Reference](README.md)
- [Syntax Index](../../syntax-index.md)

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
