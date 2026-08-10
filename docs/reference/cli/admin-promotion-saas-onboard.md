# etl-sql admin promotion saas-onboard

Create and populate one physically isolated SaaS tenant boundary

## Synopsis

```text
etl-sql admin promotion saas-onboard [options]
```

## Options

| Option | Description |
| :--- | :--- |
| `--bind` | Target binding in SOURCE=TARGET form (repeatable). |
| `--max-concurrent-jobs` | Tenant concurrent-job limit. |
| `--max-report-sessions` | Tenant concurrent report-session limit. |
| `--max-storage-mb` | Tenant storage limit in MiB. |
| `--oidc-authority` | Tenant-owned OIDC issuer HTTPS authority. Must be paired with --oidc-client-id. |
| `--oidc-client-id` | Tenant-owned OIDC client id. Its secret is injected at Portal__Identity__Oidc__ClientSecret. |
| `--output-root` | Deployment-plane root under which the isolated tenant boundary is created. |
| `--package, -p` | Path to a versioned Orchestrator promotion package. |
| `--portal-bootstrap` | Optional secret-free Portal configuration bootstrap to stage for tenant replay. |
| `--source, -s` | Workspace or export root to inventory (default: current directory). |
| `--source-profile` | Onboarding source profile: Solo or Enterprise. |
| `--tenant` | Tenant assertion; must match the active signed onboarding authorization. |

## References

- [CLI Reference](README.md)
- [Syntax Index](../../syntax-index.md)

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
