# Governance Core

### 4.4 Governance Core

Governance Core centralizes three production controls:

- **Plaintext secrets policy enforcement** — the central linter detects and blocks plaintext secret persistence when forbidden by policy.
- **Named secret references** — connector passwords and sensitive connection-string fields can use `SECRET:name` instead of raw secret values.
- **Durable audit forwarding** — Portal security and mutation audit rows are staged in a transactional outbox and can be forwarded to an HTTPS collector, with optional fail-closed behavior.

#### Named secret providers

Configure the secret provider in `appsettings.json` or with environment variables under `Governance:Secrets:*`.
The older `Secrets:*` prefix remains accepted as a compatibility fallback, but new deployments should use
`Governance:Secrets:*`.

```json
{
  "Governance": {
    "Secrets": {
      "Provider": "Environment",
      "EnvironmentPrefix": "ETLSQL_SECRET_"
    }
  }
}
```

Supported providers:

| Provider | Required settings | Operational notes |
| :--- | :--- | :--- |
| `Environment` | Optional `EnvironmentPrefix` | Secret names are uppercased; `.` and `-` become `_`. With the prefix above, `SECRET:sales_db_password` resolves from `ETLSQL_SECRET_SALES_DB_PASSWORD`. |
| `OsSecretStore` | `OsStoreRoot` | Stores protected values under a fully qualified local directory. Values are encrypted machine-scoped (DPAPI `LocalMachine` on Windows, machine-id-derived AES-256-GCM elsewhere), so an administrator can write secrets that a differently privileged service account reads back; restrict the directory with filesystem ACLs since any account on the host that can read the files can decrypt them. On Unix, secret files are written owner-read/write only. Values written by releases before machine scoping are user-scoped and stay readable by the account that wrote them; rotating a secret upgrades it to machine scope. The store is never read as plaintext — unrecognized file contents fail closed. |
| `HttpsVault` | `VaultEndpoint`; optional `VaultBearerToken` | The endpoint must be HTTPS. The provider requests `<VaultEndpoint>/<secret-name>` and accepts either a raw response body or JSON `{ "value": "secret" }`. |
| `PortalStore` | none (Portal host only) | Stores secrets encrypted in the Portal database using the cluster-wide Data Protection key ring — the supported multi-node path without an external vault. Managed through `api/admin/secrets` (set, list metadata, verify, verify-all, disable, enable, delete; values are never returned after write, and every mutation is audited). The `secret-store-keyring` health check under `GET /health` decrypt-probes every stored secret so an HA node with a wrong `Portal:Storage:KeyRingPath` fails fast; run `POST api/admin/secrets/verify-all` after a backup/restore to prove the restored key ring can decrypt every value without printing them. Not available to standalone CLI/Orchestrator deployments. |

Environment-variable examples:

```text
Governance__Secrets__Provider=HttpsVault
Governance__Secrets__VaultEndpoint=https://vault.example.com/etl-sql/secrets
Governance__Secrets__VaultBearerToken=ENC:ENCRYPTED_TOKEN
```

#### Managing OS secret store secrets from the CLI

With `Governance:Secrets:Provider=OsSecretStore` configured, administrators manage named secrets
without touching secret files directly:

```powershell
etl-sql admin set-secret --name sales_db_password      # prompts (masked) and confirms
etl-sql admin verify-secret --name sales_db_password   # proves the secret resolves; never prints it
etl-sql admin rotate-secret --name sales_db_password   # replaces the value; fails if it does not exist
etl-sql admin disable-secret --name sales_db_password  # resolution fails until re-enabled
etl-sql admin enable-secret --name sales_db_password   # re-enables; the stored value resolves again
etl-sql admin delete-secret --name sales_db_password   # permanently removes the secret
```

`set-secret` and `rotate-secret` read the value from a masked interactive prompt (with
confirmation), or from stdin when input is piped (`Get-Content value.txt | etl-sql admin
set-secret --name x`). `--value` is supported for automation but can persist in shell history —
the CLI warns when it is used. Values are encrypted machine-scoped before they reach disk, so run
these commands on the machine that will resolve the secrets. `set-secret` on a disabled secret
re-enables it. Secret values are never echoed, logged, or included in error messages.

#### Shared connections (connection catalog)

An administrator can catalog a connection once — the SSRS shared data source model — so users
reference it without knowing the credentials. Configure the catalog with
`Governance:ConnectionCatalog:Provider=Local` and `Governance:ConnectionCatalog:LocalRoot=<dir>`
(machine-encrypted entries, same trust boundary as the OS secret store — restrict the directory
with filesystem ACLs), then manage entries from the CLI:

```powershell
etl-sql admin set-connection --alias sales_dw --type MSSQL --option SERVER=sql01 --option DATABASE=Sales --option USER=etl_worker --option PASSWORD=SECRET:sales_db_password
etl-sql admin set-connection --alias archive_s3 --type S3 --option BUCKET=archive-prod --option ACCESS_KEY=SECRET:archive_access_key --option SECRET_KEY=SECRET:archive_secret_key --sensitive BUCKET
etl-sql admin list-connections                       # aliases and Active/Disabled status
etl-sql admin verify-connection --alias sales_dw     # proves the entry and its SECRET: references resolve
etl-sql admin disable-connection --alias sales_dw    # SHARED:sales_dw fails until re-enabled
etl-sql admin enable-connection --alias sales_dw     # re-enables; the stored definition is retained
etl-sql admin delete-connection --alias sales_dw
```

Catalog entries hold `SECRET:name` references, never credential values — `set-connection` rejects
raw credentials and points at `set-secret`. Scripts use the entry through the declared connector
type, which must match the catalog entry:

```sql
CREATE CONNECTION dw AS MSSQL('SHARED:sales_dw');
```

At execution the alias expands to the cataloged definition, `SECRET:` references resolve through
the configured secret provider, and script-local options may add to but never override cataloged
credential fields or catalog-owned sensitive fields. An unknown alias, a disabled entry, a connector type mismatch, or an
unconfigured catalog fails connection creation with a clear error.

For multi-node deployments, set `Governance:ConnectionCatalog:Provider=Portal` on the Portal so
entries live in the Portal database (endpoints and options are additionally encrypted at rest with
the cluster keys) and are managed through the audited `api/admin/connections` API: list, masked
detail, set (raw credential values rejected), verify (proves the entry and its `SECRET:`
references resolve, and stamps last-verified), disable, delete, and metadata-only export/import
for promoting entries between environments. Entries record owner, environment scope, and
last-used/last-verified timestamps for governance review. Pair it with
`Governance:Secrets:Provider=PortalStore` so both the catalog and the secrets it references are
cluster-wide.

Portal-cataloged entries can additionally carry **use grants** (Admin → Connections → Detail →
Access, or `api/admin/connections/{alias}/acl`): an entry with no grants is usable by any caller
(the default), while an entry with grants can only be expanded by administrators, its owner, or
members of a granted group. The executing user's identity is checked at `SHARED:alias` expansion
time, denials are audited (`SHARED_CONNECTION_USE_DENIED`) without resolving any secret, and
executions without an injected identity are denied for restricted entries. Grants are group-based,
matching the folder/dataset permission model.

Before disabling or deleting, check **impact** (the Impact button in either admin tab, or
`GET api/admin/connections/{alias}/impact` / `GET api/admin/secrets/{name}/impact`): it lists
published reports, subscription job scripts, and orchestrator scheduled jobs whose scripts
reference the alias or secret name, catalog entries that reference a secret, and — for shared
connections — the recorded per-consumer usage (which user resolved the entry, when, and how many
times), captured automatically at `SHARED:alias` resolution.

