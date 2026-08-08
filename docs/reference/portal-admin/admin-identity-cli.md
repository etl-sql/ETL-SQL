# Admin Identity CLI

`etl-sql admin portal-whoami`, `admin user …`, `admin group …`, and `admin session list` administer a
Portal's users and groups from a terminal, so provisioning is scriptable and reviewable in a diff
rather than a sequence of clicks.

The per-verb option tables are generated from the command tree — see
[the CLI reference](../cli/admin.md). This page covers the parts that are contract rather than
syntax: how credentials are supplied, what the exit codes mean, and what the verbs can and cannot
reach.

## What it can reach

These verbs run against the Portal's administration API over **HTTP**, using a service account that
holds the `admin.identity` scope. That scope is narrow on purpose: it reaches identity
administration and nothing else. Backup and restore, configuration export, environment promotion,
support bundles, audit export, service restart and shutdown, and dataset key rotation are all
unreachable by any token — see [Service Accounts](service-accounts.md) for the reachable-route list
and the reasoning.

Going over the wire is deliberate. It is what lets the CLI administer a **remote** Portal from a jump
box; an architecture test asserts `ETL-SQL.App` never takes a project reference on the Portal, so
this cannot regress into a same-host-only tool.

## Credentials

| Input | Source |
| :--- | :--- |
| Portal URL | `--portal-url`, else `ETLSQL_PORTAL_URL` |
| Client id | `--client-id`, else `ETLSQL_PORTAL_CLIENT_ID` |
| Client secret | `ETLSQL_PORTAL_CLIENT_SECRET` **only** |

**The secret is never accepted as a command-line argument.** A command line is readable by every
process on the host, is written to shell history, and is captured verbatim in CI logs. The client id
is an identifier rather than a credential, so it may be passed as a flag.

The secret variable may hold a literal value or a `SECRET:name` reference, which is resolved through
the machine-local secret store:

```bash
export ETLSQL_PORTAL_URL=https://portal.example.com
export ETLSQL_PORTAL_CLIENT_ID=sa_9f2c1b...
export ETLSQL_PORTAL_CLIENT_SECRET=SECRET:portal-admin

etl-sql admin portal-whoami
```

`portal-whoami` resolves the credentials, prints the identity, roles, scopes, and token expiry, and
prints no secret. It is the first thing to run when a runbook fails.

### Bootstrapping

The CLI's own credential is a service account, so the **first** one is created interactively in the
Portal — there is no chicken-and-egg bootstrap that needs a token to mint the first token. The
account must hold both the `Admin` role and the `admin.identity` scope; the Portal refuses `Admin`
without that scope.

## Exit codes

Scripts branch on these, so they are part of the contract.

| Code | Name | Meaning |
| ---: | :--- | :--- |
| 0 | Success | |
| 3 | AuthFailure | Credentials missing, malformed, or rejected |
| 4 | ScopeDenied | Authenticated, but the token lacks the scope or role for that route |
| 5 | NotFound | The named user, group, or session does not exist |
| 6 | AmbiguousMatch | A name matched more than one record; disambiguate by id |
| 7 | Conflict | The record changed since it was read, or a uniqueness constraint failed |
| 8 | ValidationError | The Portal rejected the request as invalid |
| 9 | Unreachable | The Portal could not be reached |

`NotFound` and `AmbiguousMatch` are deliberately distinct. A runbook can reasonably create a missing
user, but must never guess which of two matches was meant — collapsing both into one generic failure
is what makes an automation loop do the wrong thing quietly.

## Output

Every read verb prints a human-readable table by default and a stable JSON document under `--json`,
matching `admin list-connections`:

```bash
etl-sql admin user list --role Publisher
etl-sql admin user list --json | jq -r '.[] | select(.isActive) | .userName'
etl-sql admin group members --name "Finance Analysts" --json
```

## Writing safely from a runbook

Two properties make this worth using instead of a browser.

**Idempotence.** `--if-not-exists` on create and `--if-exists` on delete turn a re-run into a no-op
rather than an error, so a provisioning script can be run twice, or resumed after a partial failure,
without a human deciding which steps to skip:

```bash
etl-sql admin group create --name "Finance Analysts" --if-not-exists
etl-sql admin user create --username jsmith --email jsmith@corp.local \
    --role Publisher --password-stdin --if-not-exists < /run/secrets/initial-password
etl-sql admin group add-member --name "Finance Analysts" --username jsmith
```

Membership changes are idempotent unconditionally — adding an existing member is exactly what a
re-run does, so it needs no flag.

**Optimistic concurrency.** Every guarded write sends the record's version in `If-Match`. By default
the CLI carries through the version it just read, so a concurrent edit is a detectable conflict
(exit 7) rather than a silent overwrite. `--if-version` pins an expected value when the caller wants
to fail on any drift at all:

```bash
etl-sql admin user disable --username jsmith --if-version 7
```

Last-writer-wins is the wrong default for an administration tool, so there is no way to ask for it.

**Passwords are never arguments.** `--password-stdin` is the only way to supply one; there is no
`--password` flag, and a test asserts there never will be.

## Answering "why can this person see this"

```bash
etl-sql admin user permissions --username jsmith
```

Resolves the name, reads the user's effective permissions, and prints them without a browser. This
is the verb worth reaching for during an access question.

## References

- [Service Accounts](service-accounts.md) — scopes, the `admin.identity` route list, and the rule
  that no token may create or promote an `Admin`
- [CLI Reference](../cli/admin.md)
