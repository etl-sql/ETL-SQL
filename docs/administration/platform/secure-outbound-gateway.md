# Secure Outbound Data Gateway

The Gateway lets ETL-SQL reach private databases, file roots, and APIs **without inbound firewall
exceptions**. It is an outbound-connected, tenant-attested policy enforcement point — not a VPN, a
SOCKS proxy, a raw TCP relay, or a remotely configurable host/port forwarder.

The executable path is shipped: setup consumes the Portal's one-time enrollment, seals an ECDSA
workload key to the machine, and the foreground daemon proves possession with a fresh signed
challenge before Portal registers it. Portal enrollment and Gateway write outcomes are durable;
Gateway-local resources are stored machine-protected and are administered only on the Gateway.

## What a script sees

Scripts name a governed alias and nothing else:

```sql
CREATE CONNECTION sales AS POSTGRES('SHARED:sales_prod');
```

The catalog resolves `sales_prod` to either a **direct** binding (a target the execution plane may
reach itself) or a **Gateway** binding:

```text
tenant connection alias:  sales_prod
  -> tenant Gateway:       hq-gateway
  -> registered resource:  corp-sql-sales
  -> Gateway-local target: MSSQL myserver:1433 / Sales     (never leaves the Gateway)
  -> Gateway-local secret: sales-etl-credential            (never leaves the Gateway)
```

Promotion between environments changes the binding, never the script. There is **no** script syntax
that requests Gateway routing or a local bypass — routing is an administrative fact.

> The unrelated `CREATE BINDING x AS GATEWAY (...)` statement is a validation-only stub for governed
> `EXECUTE TOOL` metadata. It is explicitly **not** an authorization or resource boundary and has
> nothing to do with this feature, despite the similar wording.

## Why the cloud side stores so little

A Gateway binding carries the connector type plus immutable Gateway and resource IDs. It cannot
store a target or a credential, and the catalog store refuses an entry that tries:

| Rejected on a Gateway-bound entry | Reason |
| :--- | :--- |
| A target of any kind | Anything in the target position is a destination |
| `HOST`, `SERVER`, `ADDRESS`, `ENDPOINT`, `URL`, `URI`, `PORT`, `DSN`, `DATA SOURCE`, … | Names a physical destination |
| `PASSWORD` and other credential fields | Credentials are held only on the Gateway |

If the cloud side could hold both the private address and the key to reach it, a compromised catalog
would hand over everything the Gateway exists to protect.

If no authenticated Gateway data plane is running, a Gateway-bound alias **fails closed**. It never
falls back to a direct connection.

## Install and start

Issue the enrollment in Portal's Gateway administration page, then run the generated command on the
on-premises host:

```powershell
etlsql gateway setup --portal https://portal.example.com --tenant tenant-a `
  --gateway-id hq-gateway --token ONE_TIME_TOKEN --non-interactive
```

The token is consumed over HTTPS and is never written to configuration. The config contains only
the broker identity plus a machine-protected PKCS#8 private key. Start the daemon in the foreground:

```powershell
etlsql gateway start
```

For unattended operation, use
`deploy/windows/install-etlsql-gateway.ps1` on Windows or install
`deploy/systemd/etlsql-gateway.service` on Linux. The service account must own its ETL-SQL local
application-data directory and the environment variables referenced by its resources.

## Register local resources

Targets and credentials never pass through Portal. Propose locally, inspect, then approve:

```powershell
$env:ETLSQL_GATEWAY_SALES = 'local-password-or-structured-secret'
etlsql gateway resource propose --resource-id corp-sql-sales --connector POSTGRES `
  --target 'Host=db.corp.internal;Database=Sales;Password=${CREDENTIAL}' `
  --credential-ref ENV:ETLSQL_GATEWAY_SALES --operations READ,WRITE
etlsql gateway resource approve --resource-id corp-sql-sales
etlsql gateway resource list
```

To enable asserted viewer context on PostgreSQL, configure the same Base64 256-bit-or-longer
`ETLSQL_VIEWER_CONTEXT_HMAC_KEY` in the Portal and Gateway secret stores. Give the resource an
expected PostgreSQL `session_user` for audit:

```powershell
etlsql gateway resource propose --resource-id corp-pg-reports --connector POSTGRES `
  --target 'Host=db.corp.internal;Database=Reports;Password=${CREDENTIAL}' `
  --credential-ref ENV:ETLSQL_GATEWAY_REPORTS --operations READ `
  --executing-credential-id svc_reporting --viewer-claims viewer_groups,viewer_roles `
  --viewer-context-ttl-seconds 60
```

This does not delegate the viewer identity to PostgreSQL. PostgreSQL authenticates the service
credential; the signed viewer values are parameterized, transaction-local application context.
OIDC roles and groups never select PostgreSQL roles. See
[Verified Viewer Context](../../architecture/decisions/verified-viewer-context.md).

`list` exposes the ID, state, connector, and operation classes only. Disablement is immediate for
new operations:

```powershell
etlsql gateway resource disable --resource-id corp-sql-sales
```

## Administrative workflow

1. A **tenant administrator** issues a one-time enrollment for a named Gateway.
2. An **on-premises administrator** installs the Gateway and consumes the enrollment **exactly
   once**, establishing an asymmetric workload identity. Only a hash of the token is ever stored, so
   reading the enrollment record does not let you enrol a Gateway. Expired, revoked, already-consumed,
   and wrong-tenant presentations all return the same message — distinguishing them would tell a
   holder of a stolen token which enrollments are worth attacking.
3. The on-premises administrator registers resources with stable IDs, a local target, a local
   credential reference, an operation class, and limits. **Discovery can propose but never approve.**
   A proposed resource neither executes nor appears in the tenant catalog, and discovery cannot
   redefine one that is already approved.
4. The Gateway publishes bounded, non-secret metadata: resource ID, connector type, allowed
   operations, limits, and state. Never the target, never the credential.
5. A tenant administrator maps an approved resource to an alias and grants principals use of it.
   **No grant means deny** — there is no implicit tenant-wide access.
6. Runs record tenant, actor, alias, Gateway/resource IDs, operation class, policy version, counts,
   result, and correlation ID, without secrets or payloads.
7. Disabling an alias or resource, or revoking the Gateway, denies on the **next** evaluation. There
   is no grace window, and a disabled resource cannot be revived by approving it again.

Platform operators receive aggregate service health. They cannot create tenant mappings, approve
local destinations, read local credentials, or grant themselves resource use.

## What has to agree before anything routes

A request routes only when **all seven** of these agree. Any one disagreeing is a denial:

1. execution tenant, 2. capability tenant, 3. Gateway identity tenant, 4. catalog binding,
5. resource ownership and approval, 6. actor grant for that operation class, 7. policy version.

Knowing another tenant's alias, Gateway, resource, and operation is not enough — a cross-tenant
request fails on the enrollment clause regardless.

## The operation channel

The Gateway dials **out** over `wss://` and never listens. An unencrypted broker URI is refused
outside loopback, and any scheme other than the typed protocol is refused outright.

The frame model has no field for a host, port, scheme, path, command, or connection string. A
compromised cloud side cannot ask the Gateway to reach an arbitrary destination because the protocol
cannot express the request. Every operation names a registered resource ID and an operation class.

Bounds — deadline, request/response size, row count, concurrency, buffering — are mandatory and have
no "unlimited" value. A resource's registered limits can only **narrow** what the cloud asked for,
never widen it. Local failures cross back as a fixed message: a provider exception naming a host, a
user, or a password never reaches the cloud side.

### Reconnect and the ambiguous-write rule

Reconnect keys off operation IDs against a durable outcome ledger:

| Recorded state | On reconnect |
| :--- | :--- |
| Committed | Return the recorded outcome; do not run it again |
| Failed | Safe to retry — the effect did not apply |
| In flight, read-only | Simply re-run; repeating a read changes nothing |
| In flight or ambiguous, **mutating** | **Escalate.** Never retried, never reported as failed |

That last row is the important one. A blind retry can double-apply a write, and calling it failed
tells you the data is not there when it may be. A committed outcome is final and cannot be downgraded
by a late ambiguous report.

## See also

- [SaaS Tenant Isolation Architecture §11](../../architecture/saas-tenant-isolation.md#11-secure-outbound-data-gateway)
- [Configuration reference](appsettings-reference.md)
