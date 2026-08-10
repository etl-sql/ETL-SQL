# Admin Identity CLI

`etl-sql admin portal-whoami`, `admin user …`, `admin group …`, `admin session …`, and
`admin service-account …` administer Portal identity from a terminal, so provisioning is
scriptable and reviewable in a diff rather than a sequence of clicks.

The per-verb option tables are generated from the command tree — see
[the CLI reference](../cli/admin.md). This page covers the parts that are contract rather than
syntax: how credentials are supplied, what the exit codes mean, and what the verbs can and cannot
reach.

## What it can reach

These verbs run against the Portal's administration API over **HTTP**, using a service account that
holds the `admin.identity` scope. That scope is narrow on purpose: it reaches identity
administration and nothing else. Backup and restore, configuration export, environment promotion,
support bundles, audit export, service restart and shutdown, and dataset key rotation are all
unreachable through `admin.identity`. Configuration export has a separate, read-only
`admin.portability` scope; the other surfaces remain unavailable to service tokens. See
[Service Accounts](service-accounts.md) for both explicit route lists and the reasoning.

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

After bootstrap, that account may manage service accounts only through constrained delegation. A
service identity can see only accounts owned by its own human owner, and can create, update, rotate,
or revoke an account only when the target's scopes, roles, and Studio capabilities are subsets of
the caller's current effective grant. It cannot select a stronger owner, add authority it lacks, or
rotate a stronger sibling to obtain its secret. A signed-in tenant administrator is not subject to
that delegation cap.

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
matching `admin machine connection list`:

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

**Passwords are never arguments.** `--password-stdin` is the only way to supply one, on both
`user create` and `user reset-password`. There is no `--password` flag, and tests assert that
neither it nor `--client-secret` parses.

```bash
etl-sql admin user reset-password --username jsmith --password-stdin < /run/secrets/new-password
```

**Partial updates only touch what you name.** `user update` and `group update` send only the fields
actually supplied, so changing an email cannot silently blank a name that was never mentioned.

**`set-capabilities` replaces, it does not add.** The grant is written wholesale, matching the API,
and passing no `--capability` clears it:

```bash
# Grant exactly these two, removing anything else the group had
etl-sql admin group set-capabilities --name "Finance Analysts" \
    --capability studio.author --capability studio.publish

# Revoke everything
etl-sql admin group set-capabilities --name "Finance Analysts"
```

Read the current grant first with `etl-sql admin group capabilities --name "…"`, which also lists
what is available.

## Service-account lifecycle and one-time secrets

The service-account verbs use the same Portal URL, client ID, environment-only client secret,
stable exit codes, and optimistic concurrency contract as the other identity verbs:

```bash
etl-sql admin service-account list

etl-sql admin service-account create --name nightly-loader --owner tenant-admin \
    --scope orchestrator.execute --role Operator \
    --expires-at 2027-01-31T23:59:59Z \
    --secret-out /run/secrets/nightly-loader.secret

etl-sql admin service-account update --name nightly-loader \
    --scope orchestrator.execute --disable

etl-sql admin service-account rotate-secret --name nightly-loader \
    --secret-out /run/secrets/nightly-loader.rotated.secret

etl-sql admin service-account revoke --name nightly-loader
```

`create` and `rotate-secret` require `--secret-out`. The CLI reserves that file with create-new
semantics before asking the Portal to mint a credential, refuses to overwrite an existing file,
uses user-only mode on Unix, and removes its empty reservation if the API request fails. The secret
is written without a trailing newline and is never printed in human or JSON output. JSON returns
account metadata plus `secretWrittenTo`, never `clientSecret`. On Windows, place the file in a
directory whose ACL already grants access only to the intended service identity.

`update` replaces the scope set when `--scope` is supplied and replaces the Studio capability set
when `--capability` is supplied. Use `--clear-capabilities` to remove every capability and
`--clear-expiry` to remove an expiry. `--enable` and `--disable` are mutually exclusive. Roles,
owner, name, and description are immutable after creation; provision a replacement when those
identity properties must change. Revocation is permanent and idempotent.

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
