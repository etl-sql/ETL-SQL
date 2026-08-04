# Seeded Acceptance Profile

A small, reproducible dataset for checking that a Portal deployment actually works — after an
install, after an upgrade, or when comparing a local run against the container image.

```powershell
# Seed and check a local Portal
pwsh -File scripts/Invoke-AcceptanceProfile.ps1 -BaseUrl http://localhost:5000 `
    -ScriptRootPath ./Reports

# Check an already-seeded environment without changing it
pwsh -File scripts/Invoke-AcceptanceProfile.ps1 -BaseUrl http://localhost:8080 -SmokeOnly
```

Exit codes: `0` all checks passed, `1` one or more failed, `2` the Portal was not reachable.

---

## Why it is small

An acceptance dataset that takes ten minutes to seed is one people stop seeding, and a large one
hides the failure it was meant to reveal among rows nobody reads. The profile is the minimum that
exercises the paths worth checking:

| Seeded | Proves |
| :--- | :--- |
| A folder | The catalog and folder permissions are working |
| A self-contained report | The script root, the engine, and the execution pipeline are wired end to end |
| One user per role — Viewer, Publisher, DataSteward, OrchestratorManager | Role assignment works, and each role's journey can be walked by hand |

The report deliberately needs no connection and no parameters, so it runs identically on any host.

---

## Why it runs over HTTP

Everything goes through the public API. That is what makes `dotnet run` and the production container
image comparable: "it passed locally" and "it passed in the image" are then statements about the
same checks, rather than two different scripts that happen to share a name.

It also means the script needs nothing installed on the target and works against a remote
environment.

---

## Idempotency

Re-running reports what already exists rather than failing or duplicating it, so it is safe against
a long-lived environment. The forced first-run password change is handled automatically, and the
script signs in correctly whether or not it has already run.

---

## The one thing it cannot do over HTTP

Publishing a report requires the `.rptsql` file to already exist under the Portal's configured
`ScriptRootPath`. An HTTP client cannot put a file there.

- **When the root is reachable from the machine running the script** — a local run, or a container
  with the root bind-mounted — pass `-ScriptRootPath` and the script writes the file itself.
- **When it is not**, the report is reported as **skipped**, not failed. A check that fails for
  something the script itself said it could not set up is noise, and noise is what stops people
  reading output.

> [!NOTE]
> `-ScriptRootPath` must be the path the **Portal** resolves, which is not always the one you passed
> on the command line: `appsettings.Development.json` overrides environment variables, so a
> development host may be reading from somewhere other than you expect. Check the effective value
> before concluding the publish path is broken.

---

## First-run configuration

Against an empty database the Portal refuses to start without a first-run administrator. Set both
before the first launch, or the host exits during migration:

```
Portal__FirstRun__AdminUsername=admin
Portal__FirstRun__AdminPassword=<password>
Portal__Jwt__Secret=<base64 secret>
```

The script performs the forced password change on first use and remembers the new password for
subsequent runs (`-NewAdminPassword` to override).
