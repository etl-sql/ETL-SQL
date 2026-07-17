# etl-sql enterprise enroll

Enroll this machine in authoritative enterprise policy

## Synopsis

```text
etl-sql enterprise enroll [options]
```

## Options

| Option | Description |
| :--- | :--- |
| `--allow-offline-failure` | Record non-fail-closed policy availability behavior for non-production enrollment. |
| `--client-certificate-thumbprint` | Optional SHA-1 or SHA-256 machine/client certificate thumbprint. |
| `--max-offline-hours` | Maximum age of cached policy before secure startup fails (1-720). |
| `--policy-endpoint` | Authoritative HTTPS organization-policy endpoint. |
| `--service-identity` | Optional Windows service identity granted read access to enrollment. |
| `--signing-key` | Path to the organization's RSA policy-signing public key in PEM format. |
| `--tenant` | Enterprise tenant or environment identifier. |

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
