# etl-sql admin orchestrator

Manage per-object Orchestrator grants and ownership

## Synopsis

```text
etl-sql admin orchestrator <subcommand>
```

## Subcommands

| Subcommand | Description |
| :--- | :--- |
| [`adopt`](admin-orchestrator-adopt.md) | Assign an owner to every unowned object (administrators only) |
| [`grant`](admin-orchestrator-grant.md) | Grant a principal a permission on an object |
| [`revoke`](admin-orchestrator-revoke.md) | Revoke a principal's grant on an object |
| [`set-owner`](admin-orchestrator-set-owner.md) | Reassign an object's owner (administrators only) |
| [`show`](admin-orchestrator-show.md) | Show the grants on one Orchestrator object |
| [`unowned`](admin-orchestrator-unowned.md) | List objects with no recorded owner — reachable only by administrators |

## References

- [CLI Reference](README.md)
- [Syntax Index](../../syntax-index.md)

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
