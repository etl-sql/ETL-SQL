# ORCHESTRATOR

Admin service connector for an ETL-SQL Orchestrator service. Does not transfer data — statements inside
an `EXECUTE orch BEGIN ... END` block are dispatched to the Orchestrator's REST API for remote job
management.

Aliases: `ORCH`

## Options

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `HOST` | Orchestrator base URL (e.g. `http://orch-server:5001`) | Yes |
| `PORT` | Override port when `HOST` has no port | No |
| `API_KEY` | Orchestrator API key (use `'SECRET:name'` in production) | No |
| `PORTAL_HOST` | Portal to obtain a signed identity from. Required for a federated Orchestrator | No |
| `USER` / `PASSWORD` | Portal account, for local and LDAP identities | No |
| `CLIENT_ID` / `CLIENT_SECRET` | Portal service account, for unattended work and OIDC deployments | No |

## Authentication

There are two postures, matching the two deployment shapes.

**Solo — a shared key.** `API_KEY` (or `PASSWORD` on its own, an alias the `PORTAL` connection syntax
established) authenticates the connection as the deployment. There is no identity, so there is nothing
to authorize per object; this is refused by an Orchestrator that requires federated identity.

**Team and above — a Portal-issued identity.** Add `PORTAL_HOST` and a credential. The connection
authenticates to the **Portal**, which is the single control plane for who exists and what they may do,
and exchanges its session for a short-lived assertion addressed to the Orchestrator. The Portal's own
session token is never presented to the Orchestrator: the two are deliberately audience-separated so
neither can be replayed at the other service. The assertion is cached and renewed shortly before it
expires.

Supply **either** `USER`/`PASSWORD` **or** `CLIENT_ID`/`CLIENT_SECRET`. Use the client-credential form
for unattended work, and on an OIDC deployment where a federated user has no Portal password to place
in a connection at all. Secrets use the canonical quoted `'SECRET:name'` form.

`PASSWORD` means the Orchestrator API key on its own, and the user's Portal password when `USER` is
also present — pairing it with `USER` is what disambiguates the two.

## Example

```sql
-- Solo: a shared key, no identity.
CREATE CONNECTION orch AS ORCHESTRATOR(HOST    = 'http://orchestrator.corp.example:5001',
         API_KEY = 'SECRET:orchestrator-key');

-- Team and above: a service account, federated through the Portal.
CREATE CONNECTION orch AS ORCHESTRATOR(
         HOST          = 'http://orchestrator.corp.example:5001',
         PORTAL_HOST   = 'https://portal.corp.example',
         CLIENT_ID     = 'sa_nightly_runner',
         CLIENT_SECRET = 'SECRET:nightly-runner');

EXECUTE orch BEGIN
    CREATE SCHEDULE MonthlySalesNightly ON '0 2 * * *';
    CREATE JOB MonthlySalesRefresh FOR REPORT '/Finance/Monthly Sales';
    ALTER JOB MonthlySalesRefresh ADD SCHEDULE MonthlySalesNightly;
    DROP JOB IF EXISTS MonthlySalesRefresh;
END;
```

## References

- [Service Connectors](README.md)
- [Connectors](../README.md)
- [Portal](portal.md)
- [Service Accounts](../../portal-commands/service-accounts.md) — the `orchestrator.*` scope ladder
