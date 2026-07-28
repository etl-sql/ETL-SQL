# Extended Admin Scripting

## 9. Extended Admin Scripting

The Portal connector supports script-first administration inside a remote block:

```sql
CREATE CONNECTION portal AS PORTAL (
    HOST = 'http://localhost:5000',
    USERNAME = 'admin',
    PASSWORD = ENC:...
);

EXECUTE portal BEGIN
    SHOW USERS;
    SHOW REPORTS;
END;
```

Result-producing commands can write to a temp table with `INTO #table` and also update `@@RESULT` / `@@RESULTSETS`.

### 9.0 Configuration Export (Script-First Reconstruction)

```sql
EXECUTE portal BEGIN
    EXPORT PORTAL CONFIGURATION TO 'portal_bootstrap.txt';
END;
```

Admin-only. Writes the portal's declarative configuration as a replayable bootstrap script in
dependency order: groups, users, group memberships, folders, folder ACLs, SMTP connections, report
publications, dataset metadata and grants, subscriptions, and alerts — by logical name, never
database id. Secrets are **never** exported: password-bearing statements carry `${...}`
placeholders collected in a `REQUIRED SECRETS` header, and a trailing summary lists every emitted,
skipped, and runtime-only item so nothing is omitted silently. The same script is available from
`GET /api/admin/configuration/export`, and each export writes an `EXPORT_PORTAL_CONFIGURATION`
audit event.

No real secret or security material ever appears in the export — not password hashes, encrypted SMTP
credentials, JWT/dataset-at-rest keys, Orchestrator API keys, refresh tokens, or share/embed
capability tokens. Each credential is a `${...}` placeholder you replace before import, ideally
**without putting plaintext in the file**:

- `ENV('NAME')` — resolve from an environment variable at import (preferred; nothing sensitive in the script).
- `ENC:...` — an encrypted literal, unlocked by `USE PASSWORD = ...` at import.
- `'...'` — a plaintext literal (least preferred; avoid committing).

An unsubstituted `${...}` placeholder is rejected at import before it reaches the portal (see §9.0
import behavior), so a forgotten secret fails closed rather than provisioning an empty credential.

Notes:

- The engine write-blocks script extensions (`.etlsql`, `.sql`) as control-plane protection, so
  export to a data extension such as `.txt` and rename after review when committing to source control.
- The script reconstructs **configuration only** — report `.rptsql` files, dataset caches, and
  snapshots are content and travel separately. The export ends with a **companion content manifest
  and recovery runbook** naming the three recovery paths and listing every report script to copy
  into the target script root and every dataset to re-materialize or re-publish. The three paths:
  (1) configuration — this script, the auditable clean-start path; (2) content — the manifest's
  report scripts and datasets, copied/published separately; (3) exact-state disaster recovery —
  restoring the portal and Orchestrator database/file backups, which this export does not replace.
- Replay against a fresh portal requires substituting every `${...}` placeholder first. To include
  scheduled refresh jobs, request the export with a target alias:
  `GET /api/admin/configuration/export?orchestratorAlias=orch`. Without an alias, refresh jobs are
  listed as manual follow-up rather than binding the export to a source environment.

#### Importing (replaying the bootstrap)

The bootstrap is replayed by running it as a normal script through an admin `PORTAL`
connection — substitute the `${...}` placeholders, then:

```sql
CREATE CONNECTION portal AS PORTAL (HOST = '...', USERNAME = 'admin', PASSWORD = ENC:...);
-- Preview first — no mutations, validates references and secrets:
SET WHAT_IF ON;
-- run the EXECUTE portal BEGIN … END block; the portal reports a create/skip plan per statement
SET WHAT_IF OFF;
-- run it again to apply
```

Import behavior:

- **Idempotent (safe to rerun).** Provisioning uses stable logical keys. Users, groups,
  memberships, folders, grants, SMTP connections, and report publications are create-or-skip.
  Named subscriptions use report path + name, and alerts use report path + name; those definitions
  are created, updated when configuration drifts, or skipped when already equal.
- **Fail-closed before mutation.** A missing referenced folder, group, user, or report stops the
  statement with a clear error instead of a generic portal failure, and an unsubstituted `${...}`
  secret placeholder is rejected before it is ever sent to the portal.
- **`SET WHAT_IF ON` is a validating dry-run.** Each statement reports what it *would* do
  (create / update / skip) and performs the same reference and secret validation as a real apply — without
  writing anything — so you can confirm a clean import before committing to it.

### 9.1 Report Operations

```sql
EXECUTE portal BEGIN
    SHOW REPORT 'Daily Sales' INTO #report;
    SHOW REPORT HISTORY 'Daily Sales' INTO #history;
    SHOW REPORT DEPENDENCIES 'Daily Sales' INTO #deps;
    VALIDATE REPORT SCRIPT 'C:\Reports\daily_sales.rptsql' INTO #validation;

    FAVORITE REPORT 'Daily Sales';
    FAVORITE REPORT 'Daily Sales' FOR USER 'alice';
    SHOW FAVORITES LIMIT 25 INTO #favorites;
    SHOW FAVORITES FOR USER 'alice' LIMIT 25;
    UNFAVORITE REPORT 'Daily Sales' FOR USER 'alice';
END;
```

Name lookups are case-insensitive. If multiple reports share the same name, the connector raises an ambiguity error instead of choosing one.

### 9.2 Sharing, Embedding, Saved Views, and Alerts

```sql
EXECUTE portal BEGIN
    CREATE SHARE LINK FOR REPORT 'Daily Sales' EXPIRES '2026-12-31T23:59:59Z' INTO #share;
    SHOW SHARE LINKS FOR REPORT 'Daily Sales';
    REVOKE SHARE LINK '<token>';

    CREATE EMBED TOKEN FOR REPORT 'Daily Sales' NAME 'Finance Wallboard' INTO #embed;
    SHOW EMBED TOKENS FOR REPORT 'Daily Sales';
    REVOKE EMBED TOKEN '<token>';

    CREATE SAVED VIEW 'EMEA' FOR REPORT 'Daily Sales'
        PARAMETERS (@region = 'EMEA', @start = 'D-1');
    SHOW SAVED VIEWS FOR REPORT 'Daily Sales';
    DROP SAVED VIEW 'EMEA' FOR REPORT 'Daily Sales';

    CREATE ALERT HighFailures FOR REPORT 'Ops'
        WHEN VISUAL FailureCard > 10
        WITH (DESCRIPTION = 'Failure card threshold');
    ALTER ALERT HighFailures ADD NOTIFICATION orchestrator.OpsEmail;
    SHOW ALERTS FOR REPORT 'Ops';
    DROP ALERT IF EXISTS HighFailures;
END;
```

### 9.3 Catalog, Permissions, Metrics, and Sessions

```sql
EXECUTE portal BEGIN
    SHOW RECENT REPORTS LIMIT 20 INTO #recent;
    SHOW CATALOG SEARCH 'finance' LIMIT 50 INTO #catalog;
    SHOW EFFECTIVE PERMISSIONS FOR USER 'alice' INTO #perms;
    SHOW EFFECTIVE PERMISSIONS FOR REPORT 'Daily Sales';
    SHOW EFFECTIVE PERMISSIONS FOR FOLDER '/Finance';
    SHOW PORTAL USAGE METRICS FOR 30 DAYS INTO #metrics;
    SHOW ACTIVE SESSIONS INTO #sessions;

    DISCONNECT USER 'alice';
    REVOKE TOKENS FOR USER 'alice';
END;
```

`SHOW ACTIVE SESSIONS` reports unrevoked, unexpired refresh tokens. `DISCONNECT USER` and
`REVOKE TOKENS` revoke refresh tokens and rotate the user's security stamp, so already-issued access
tokens are rejected on their next request.

### 9.4 Service Control

```sql
EXECUTE portal BEGIN
    RESTART PORTAL;
    SHUTDOWN PORTAL;
END;
```

Service-control commands require an Admin user and are disabled by default. Enable them only for trusted automation:

```json
{
  "Portal": {
    "AllowServiceControl": true
  }
}
```

`RESTART PORTAL` requests process shutdown so Docker, systemd, Windows Service, or another supervisor can start it again. The portal does not self-spawn a replacement process.

---
