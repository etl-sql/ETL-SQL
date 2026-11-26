# External Tool Use Grants and Native Artifact Approval

**Status** — XT07 catalog grants and federated Orchestrator caller projection are implemented in
the feature branch. The Portal resolves current tool principals. An unattended run uses the owner’s
tool authority after approval of the exact saved inline script version.
XT08 supports direct ELF and fixed dash script approval in an ownership-verified machine
catalog on restricted Linux workers. The Windows x64 fixture passed under a restricted virtual
service account. The feature branch wires that profile into direct PE approval and launch.
The installed Windows Orchestrator passed qualification under the restricted virtual service profile.

## Behavior before the proposal

`LocalToolCatalogProvider` accepts an `ExecutionIdentity` and reads the protected entry by alias. It
does not use the identity to authorize the read. The admin read path calls the same resolver with
a null identity. The entry serializes the tool type and options, and a store operation writes the
active entry in place. These are the boundaries to change for separate admin and invocation paths
and atomic grant updates. [Tool catalog](../../../src/ETL-SQL.Core/Governance/ToolCatalog.cs)

`OrchestratorCaller` carries a subject key, tenant, group IDs, group names and scopes. The service
projects group names into `ExecutionIdentity.Groups` for row-level predicates. A scheduled run can
reconstruct an identity from the stored job owner without current group membership. The Portal
identity builder queries the effective user by tenant and populates group names. Tool authorization
needs a stable principal projection that survives both forms of execution. [Federated caller](../../../src/ETL-SQL.Core/Governance/OrchestratorIdentityAssertion.cs) · [Orchestrator API](../../../src/ETL-SQL.Orchestrator.Service/JobApiEndpoints.cs) · [Scheduler](../../../src/ETL-SQL.Orchestrator/Scheduling/SchedulerService.cs) · [Portal identity builder](../../../src/ETL-SQL.Portal/Services/PortalExecutionIdentityService.cs)

The native handler validates `COMMAND`, `WORKING_DIR` and `SCRIPT_PATH`. It starts the resolved
command by path and builds argv separately from `ARGS`. `SCRIPT_PATH` is checked but does not bind the
script argument delivered to the interpreter. The handler also keeps a per-invocation scratch
directory separate from the configured working directory. [Tool handler](../../../src/ETL-SQL.Engine/Handlers/ExecuteToolStatementHandler.cs) · [Tool reference](../../reference/statements/ddl/create-tool.md)

## XT07 — Authorize use of each tool

### How the caller signs in

For a shared host, a person signs in to the Portal and submits a permitted script or job from
that session. The Portal derives the caller and tenant from authenticated server state. A
federated Orchestrator verifies the signed caller assertion and carries that identity into the
execution host. Automation uses a verified, scoped service principal. A shared API key
authenticates the service connection, and the caller assertion identifies the person or service
acting through it. The Solo API-key mode provides no principal for a per-tool grant.
[Orchestrator authentication](../../administration/orchestration/orchestrator-portal.md)

A standalone `etl-sql run` uses the operating-system account and its configured local vault and
catalog. The descriptor for an owner-account catalog must come from trusted host configuration.
Shared connector secrets belong on the governed execution host, where the signed-in user
receives access through the connection catalog. [Secrets by deployment profile](../../administration/platform/secrets.md)

There are three checks in a shared run. The host authorizes submission under the caller's
identity. The execution host authorizes each `SHARED:alias` through its connection catalog before
the engine resolves the connection's `SECRET:name` references. XT07 adds an explicit Use grant
check when the script invokes each tool alias. The connector credentials stay in the execution
host, and direct `SECRET:` references in connection definitions require administrator authority
on governed hosts. For the first shared fixture, use federated execution and verify that the
catalog used by the actual runner enforces the connection ACL. A signed-in author with access
to `SHARED:SalesReadOnly` can use that connection and still be denied invocation of a tool they
have not been granted. [Portal catalog resolver](../../../src/ETL-SQL.Portal/Services/PortalCatalogConnectionProvider.cs) · [Connection creation](../../../src/ETL-SQL.Engine/Handlers/CreateConnectionStatementHandler.cs) · [Secret resolution](../../../src/ETL-SQL.Engine/Services/ConnectionSecretResolver.cs)

### Host and principal

- **Shared execution.** Require an explicit allow grant for the verified effective caller under
  the host's configured identity authority and tenant. A grant names a user, group or service by
  stable ID. The tenant, principal kind and ID must match. A missing verified identity, unavailable
  principal lookup, unmatched grant, or out-of-scope service caller denies use.
- **Owner-account execution.** Keep a deliberately configured standalone catalog available to its
  OS owner. Check that the trusted host selected owner-account execution and verified the catalog's
  ownership boundary for the local account. Missing identity on a shared request cannot select
  this mode. Catalog ownership controls who can change its definition. The invocation policy
  controls who can run each tool.
- **Authority propagation.** Carry a typed principal, verified group IDs and applicable scopes
  through the existing trusted identity handoffs. Leave group names available to `HAS_GROUP`. Use
  the effective principal for authorization and the real actor for audit. Keep the existing tenant
  boundary and any stricter host policy. Preview-only identities carry no tool authority.
- **Scheduled and spawned jobs.** Resolve the persisted owner or the authorized revision submitter
  through the configured authority. Preserve the verifier's principal and constraints in the
  existing process and sandbox handoffs. A legacy owner label or synthesized role/scope set
  cannot substitute for a verified grant principal. Use current membership at scheduled admission.
  For interactive requests, group claims are bounded by the validity of the host's authenticated
  session. Removing a tool grant takes effect at the next authorization check in either case.

### Refreshing saved-job tool authority

The Orchestrator uses a Portal service account with the `orchestrator.tool-identity.read` scope to
look up a saved job's principal in the account's tenant. The Portal returns stable group keys for
an active user. It checks a service owner is active and currently has an execution scope. The
response carries neither interactive roles nor a credential for the owner. The Orchestrator can
reuse its short-lived service token and reads the principal again for each attempt.

When this lookup is configured, a saved-job trigger or resume that already carries the verified
owner's tool principal refreshes it at execution. A changed job version or an unavailable lookup
removes tool authority for that attempt. A submitted script revision uses the verified submitter.
A job manager can change the saved script while retaining the recorded owner. Only the owner may
approve the exact persisted version through the federated API. A later saved edit invalidates it.
Ownership transfer clears the approval on the same row. A scheduled attempt reads the approval for
its job ID, tenant, owner and version, then resolves current membership. Approval-derived authority
applies to tool calls in the top-level inline script.
[Operator setup](../../administration/orchestration/orchestrator-portal.md#refresh-tool-identity-for-saved-jobs)

### Catalog and invocation

Extend the protected local entry with a schema version, revision and typed grants. Keep the grant
with its tool so disabling, replacing or revoking it changes one authoritative record. Keep admin
read/write operations separate from resolution for invocation. Use the existing host-admin
`ToolAdminService` commands as the administration boundary. Grant or revoke access with
`etl-sql admin machine tool grant|revoke --name <alias> --tenant <id> --principal-kind <kind> --principal <key>`.
Use the stable key from the identity authority and `USER`, `GROUP`, or `SERVICE` for the kind. Preserve grants when editing
unrelated options, and preserve disabled state when editing grants. [Admin service](../../../src/ETL-SQL.App/App/ToolAdminService.cs)

Serialize admin mutations for one alias across processes. Prepare a protected temporary file in
the catalog directory and publish it with an atomic replacement. Readers use one complete
revision and reload it for every invocation. Fail closed on malformed or unsupported records.
Verify the replacement and verify that concurrent grant and definition edits cannot lose one
another. Avoid carrying a cached allow decision in a job or checkpoint.

Authorize before evaluating caller parameters, resolving secrets, staging inputs or creating a
process. `WHAT_IF` checks the same invocation permission before showing the plan. Keep denials
consistent for unknown and inaccessible aliases on shared hosts. Write bounded structured audit
events for changed grants, revocations and denied invocations with alias, actor, effective
principal, tenant and reason. Keep paths, arguments and secret contents out of denial output.
Revocation governs new authorizations. An invocation already authorized against a complete
revision can finish under that revision.

### Upgrade and acceptance

An existing legacy entry has no explicit grants. Shared enforcement denies it until an
administrator adds grants. The catalog list inventories the aliases. The migration guide covers
registering the intended callers, upgrading the Orchestrator and CLI workers together, and
preserving the catalog for rollback. Versioned entries reject older readers. The hosted profile
must fail closed if a caller arrives through a path that cannot project the necessary identity.

One-shot and warm CLI workers load the Orchestrator's configured local tool catalog. The parent
passes the catalog ownership settings and any dedicated-host native opt-in alongside the signed
identity handoff. Each `EXECUTE TOOL` reads the current grant, including on a reused worker.
[Spawned worker tests](../../../tests/ETL-SQL.Tests/Orchestration/ProcessCallerIdentityTests.cs)

Local Linux acceptance used the Portal and Orchestrator over loopback, a Portal-issued credential
for live membership lookup, and a machine-owned catalog registered through the admin CLI. The
approved version started a native tool in both one-shot and warm workers. The test checked for
an output marker from the native process. No marker appeared before owner approval, after the
owner lost the Portal group, after a saved edit, or after the catalog grant was revoked. Owner
approval of the edited version restored access while the group grant was active. The warm test
used the same worker throughout.

Prove a real scheduled and process-spawned fixture where one user or group can run tool A and an
independent principal can run tool B. Include an explicitly scoped service caller, a same-named
group in another tenant, a missing identity, a preview identity, an admin with no Use grant,
revocation between two invocations, and a grant edit against a disabled tool. Exercise authorized
and denied `WHAT_IF` and confirm no process or secret access on denial. Validate a local owner-only
fixture separately.

## XT08 — Approve and launch a specific native artifact version

### Publish a protected version

Use administrator-published version directories under a local protected root. The approved
catalog revision refers to one immutable version and a SHA-256 manifest. Record the executable,
any fixed script, its interpreter when present, and required tool-owned companion assets with
their relative paths, roles and digests. Approve the launch contract together with those digests,
including the fixed argv layout and the policy for permitted data arguments. A change to the
launch contract or approved bytes requires a new explicit approval.

Prepare and verify the complete version in a private administrator-owned staging directory.
Publish it at a new path and activate it by one atomic catalog update. Once published, keep the
directory in place and unmodified while any invocation can use it. Rotate by publishing a new
version. Retain previous versions until the initial implementation's maintenance window can
confirm there are no active consumers. A failed approval leaves the current version available.
Administrators can roll back by explicitly activating a retained, still-verified version.

The runtime account and its child processes must have read/execute access and lack write,
delete, rename and permission-change authority over the version and its replaceable ancestors.
Use the existing path and ownership guards as a starting point, then test actual effective
permissions for each supported OS and installation profile. On Windows include the leaf DACL
and ancestor delete-child rights. On Unix include the effective runtime account, parent
directory permissions and filesystem boundaries. Require an isolated read-only artifact view
or a separate restricted runtime account when the host's normal service identity can write the
store. Reject pinned mode if the deployment cannot establish that boundary. The trusted machine
administrator, system loader and OS-owned dependencies remain part of the host trust boundary. [Windows file security](https://learn.microsoft.com/en-us/windows/win32/fileio/file-security-and-access-rights) · [Windows file access rights](https://learn.microsoft.com/en-us/windows/win32/fileio/file-access-rights-constants)

### Direct-executable boundary probes

Linux acceptance preparation used a disposable root-owned version store and UID/GID 65534
with no effective or permitted capabilities and `NoNewPrivs` enabled. The worker read and
executed a verified ELF. The OS denied write-open, permission changes, renaming the executable
or its version directory, replacing the active approval, and adding a companion file. A worker
that had already hashed and opened v1 launched v1 through that descriptor after the administrator
switched the active approval to v2. A new launch using the protected absolute path selected v2.
Explicitly reactivating the retained v1 path worked, and changed bytes failed SHA-256 verification
before launch. This fixture exercises the separate publisher and worker boundary used by the
templated service model. [Linux service unit](../../../deploy/systemd/etl-sql-orchestrator@.service)

On Windows, a disposable application host launched while the SHA-256 verification stream held
`FileShare.Read`. Windows refused write access and a leaf rename while the stream was open. After
the stream closed, changing the executable produced a digest mismatch before the next launch.
This tests the file handle on the local development account. The MSI runs the Orchestrator as
`LocalSystem`, which has administrator access to an administrator-owned store. Qualification
requires a restricted worker identity and tests of its effective rights on the version and
every replaceable ancestor. [Windows installer](../../../src/ETL-SQL.Installer/Installer.wxs)
· [Microsoft's LocalSystem documentation](https://learn.microsoft.com/en-us/windows/win32/services/localsystem-account)

#### Windows service qualification procedure

Use a disposable Windows host with PowerShell 7 and the .NET 10 runtime available to services.
Use a fixed NTFS volume and an administrator-owned fixture parent whose ancestors prevent
replacement through delete-child or permissions changes. Set the reviewed executable paths,
distinct SHA-256 digests, and distinct standard-output markers.

```powershell
$prepare = @{
    FixtureParent = $protectedFixtureParent
    FirstExecutable = $firstReviewedExecutable
    FirstSha256 = $firstReviewedDigest
    FirstMarker = $firstExpectedOutput
    SecondExecutable = $secondReviewedExecutable
    SecondSha256 = $secondReviewedDigest
    SecondMarker = $secondExpectedOutput
}
```

##### Run the portable bundle on a disposable VM

Build the bundle on Windows for the same architecture as the VM. The two payloads are
[single-file console fixtures](../../../scripts/fixtures/windows-tool-artifact-payload/Program.cs)
that report which generation ran. The bundle command prints the archive path and SHA-256.

```powershell
.\scripts\New-WindowsToolArtifactQualificationBundle.ps1 -OutputDirectory 'C:\scratch\xt08-bundle'
```

Move the archive to the VM and compare its SHA-256 with the builder output before extracting.
Use PowerShell 7 from an elevated shell. Set `$fixtureParent` to a dedicated directory on fixed
NTFS. Administrators or LocalSystem must own it, and the service account must be able to read
permissions on it and its ancestors. Grant it only read and traverse access to the parent.
The runner creates its own protected artifact directories under that parent.

```powershell
.\Invoke-BundledWindowsToolArtifactQualification.ps1 -FixtureParent $fixtureParent |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath .\xt08-service-receipts.json -Encoding utf8NoBOM
```

The bundle checks the SHA-256 of the included files before creating a fixture. Its three
receipts must show `ServiceBoundaryVerified = True`. `-MechanicsOnly` runs the local rehearsal
and returns receipts with that field set to `False`.
[Bundle builder](../../../scripts/New-WindowsToolArtifactQualificationBundle.ps1)
· [Bundle entry point](../../../scripts/Invoke-BundledWindowsToolArtifactQualification.ps1)

##### Run with a virtual service account

Publish the test runner for the host architecture from a trusted checkout. Use an elevated
PowerShell 7 shell for the qualification command.

```powershell
$runtime = 'win-' + [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
$runnerOutput = Join-Path $PWD.Path 'scratch\xt08-service-runner'
dotnet publish .\scripts\fixtures\windows-tool-artifact-service\WindowsToolArtifactServiceFixture.csproj `
    -c Release -r $runtime --no-self-contained -o $runnerOutput
if ($LASTEXITCODE -ne 0) { throw 'Could not publish the XT08 service runner.' }

$runner = Join-Path $runnerOutput 'WindowsToolArtifactServiceFixture.exe'
& .\scripts\Invoke-WindowsToolArtifactServiceQualification.ps1 @prepare -ServiceRunner $runner
```

The launcher registers a temporary service with a dedicated `NT SERVICE\<name>` virtual
account and requests only `SeChangeNotifyPrivilege`. It runs the three probes and removes
that service on exit. It retains the reviewed artifacts and receipts under the fixture parent.
The worker can write each probe's private receipt and scratch directory. The publisher
controls the runner, probe scripts, version store, and active catalog.
[Service launcher](../../../scripts/Invoke-WindowsToolArtifactServiceQualification.ps1)
· [Windows virtual accounts](https://learn.microsoft.com/windows/security/identity-protection/access-control/service-accounts#virtual-accounts)
· [Service token privilege configuration](https://learn.microsoft.com/windows/win32/api/winsvc/ns-winsvc-service_required_privileges_infow)

Run the manual procedure with the actual intended service token if the deployment uses a
different account or privilege policy.

##### Run with a deployment-specific service account

Use a disposable service that has the intended Windows worker token. Publish both reviewed
versions and select generation 1 from an elevated shell.

```powershell
$prepare.WorkerAccount = $serviceAccount
$fixture = & .\scripts\Invoke-WindowsToolArtifactQualification.ps1 @prepare
$fixture.FixtureRoot
```

Run verification from a child process of the service so it inherits the worker token. Set
`$fixtureRoot` to the printed absolute path. Use a copy of the qualification scripts the worker can
read and only the administrator can change.

```powershell
& .\scripts\Invoke-WindowsToolArtifactQualification.ps1 -FixtureRoot $fixtureRoot
```

From the elevated publisher shell, activate generation 2 and rerun the service verification.
Reactivate generation 1 and run the same verification for rollback.

```powershell
& .\scripts\Invoke-WindowsToolArtifactQualification.ps1 -FixtureRoot $fixtureRoot -Generation 2
# Capture the generation 2 receipt under the worker token before rolling back.
& .\scripts\Invoke-WindowsToolArtifactQualification.ps1 -FixtureRoot $fixtureRoot -Generation 1
```

Retain the three receipts with `ActiveGeneration` values of 1, 2, and 1 and the corresponding
`FixtureSha256` values. Each receipt must show `ServiceBoundaryVerified = True`. The probe
checks the service token, rejects privileges that can bypass permissions, and checks the ACLs
and effective access on the executable and catalog through non-mutating opens. It verifies the
selected digest and holds the executable open for read through process start. A catalog reader
with an open snapshot retains the previous selection across an NTFS replacement.
[Qualification script](../../../scripts/Invoke-WindowsToolArtifactQualification.ps1)
· [Windows probe](../../../scripts/Test-WindowsToolArtifactBoundary.ps1)
· [Windows file and directory access rights](https://learn.microsoft.com/windows/win32/wmisdk/file-and-directory-access-rights-constants)
· [Windows file rename information](https://learn.microsoft.com/windows-hardware/drivers/ddi/ntifs/ns-ntifs-_file_rename_information)

A local rehearsal with `-MechanicsOnly` verified both digests, both markers, rotation with an
open catalog snapshot, rollback, and preservation of the active selection when activation of a
modified artifact failed. The probe also refused hard links, junctions, and an interactive token
in service mode. The mechanics receipts show `ServiceBoundaryVerified = False`.

GitHub Actions qualified the x64 fixture on image `win25-vs2026` version `20261116.315.1`, at
commit `aaac35096865b349016f2ba60a5fc30cdee99b8f`. The virtual service account passed the ACL
and effective-rights tests and launched the verified executable while holding its read handle.
All three receipts show `ServiceBoundaryVerified = True`. Rotation selected a different digest
and rollback restored the initial digest. [Successful qualification run](https://github.com/etl-sql/ETL-SQL/actions/runs/45619556120)
· [Receipts artifact](https://github.com/etl-sql/ETL-SQL/actions/runs/45619556120/artifacts/15024595139)

The current Windows MSI service runs as `LocalSystem`. The opt-in product path checks the
configured virtual service account, restricts its token privileges, and verifies effective access
on the catalog and artifact paths. The publisher verifies the one-file x64 PE version and replaces
its active catalog entry with the same NTFS rename semantics used by the fixture. The engine holds
the verified file open through process start.

#### Qualify the installed Orchestrator

Use a disposable Windows x64 VM with the candidate MSI installed and its Orchestrator feature
selected. Build the qualification bundle from the same source revision, verify the archive hash
before extraction, and run from the extracted directory. Use an elevated PowerShell 7 shell and
a protected local NTFS fixture parent that allows service accounts to traverse it.

```powershell
.\Invoke-BundledWindowsToolArtifactQualification.ps1 -FixtureParent $fixtureParent -InstalledOrchestrator
```

The script registers a temporary restricted service using the MSI's Orchestrator executable and
targets a separate loopback port. It submits real jobs using the installed CLI in one-shot mode.
Successful jobs verify the output row from the selected executable. The refusal job substitutes
the other valid executable in the active generation and requires a SHA-256 failure. A rejected
reapproval must leave the catalog entry unchanged. The retained receipts identify the installed
binaries by hash and record the initial selection, rotation, tamper refusal, recovery, and rollback.
The runner checks that the restricted service persists its child-process ledger in the configured
log directory and clears its entries when the jobs finish.
The script removes its service and temporary credentials.

On November 25, 2026, GitHub Actions qualified the Windows x64 candidate MSI built from
`d1f1faa92f78df01f41a6199b6cf8d529f8d4d07`, with SHA-256
`8ed8b0973d0f2ac87ba068a0ec1ac7ae638540ec9ed996dc540f4e9a5b0e9f5f`.
The installed CLI and Orchestrator activated generation 1, rotated to generation 2, and refused
the tampered executable. Rejected reapproval preserved the catalog entry. The service ran
generation 2 again after the executable was restored, rolled back to generation 1, and left an
empty child-process ledger under the restricted virtual service account.
[Qualification run](https://github.com/etl-sql/ETL-SQL/actions/runs/45701102265) · [Candidate hashes and receipts](https://github.com/etl-sql/ETL-SQL/actions/runs/45701102265/artifacts/15064795079)

### Linux product validation

The Linux runner launches the selected absolute path in a root-owned version directory.
The catalog update changes only the active selection. The worker lacks the rights to replace the
selected generation across verification and launch. Administrators publish on the same local
filesystem and retain every generation that may still be in use.
[Operator procedure](../../administration/orchestration/orchestrator-portal.md#approve-native-tools-on-a-restricted-linux-host)

The product CLI fixture used the machine tool admin command as root and ran the CLI as UID 65534
with capabilities cleared and `NoNewPrivs` enabled. Real tool processes wrote fixture markers.
A privileged worker, mismatched digest, group-writable executable and added companion were
refused. Failed activation left the original selection available. Changing the arguments or
rotating the executable blocked `--resume` at a named checkpoint. A fresh session used the new
file, and reactivating the original approval restored the first session.

The session's SQLite ledger stores the generation digest and launch contract identity in an
insert-only alias binding. A resume compares that binding before launching the tool. The logical
operation ID keeps its existing fields. The alias binding still detects rotation if a new contract
would change that operation ID. [Tool execution and ledger](../../../src/ETL-SQL.Engine/Handlers/ExecuteToolStatementHandler.cs)

### Fixed dash launch

The approved generation contains the reviewed `dash` executable and one fixed shell script.
Its generation digest binds both file digests. The launch contract includes the absolute entry
point and the reviewed data argument template. The worker verifies both files before starting
the interpreter. It supplies the verified script path as the first argument and forwards
the remaining approved values as positional arguments. The noninteractive launch clears
inherited shell startup variables. [Operator procedure](../../administration/orchestration/orchestrator-portal.md#approve-a-fixed-dash-script)

The restricted Linux CLI fixture denied option-shaped caller data, write access to either file,
changes to the interpreter or script digest, and unexpected files in the generation. A rotation
required the original approval for named checkpoint recovery. A loopback Portal and Federated
Orchestrator exercised the same fixed script with both one-shot and warm workers as UID 65534.
Owner approval enabled the first run, changed script bytes blocked later runs, and publishing a
new generation restored execution. Removing the caller's group or catalog grant stopped execution.

### Bind verification to process launch

1. Authorize the caller and select one catalog revision and artifact version. Validate that the
   version path and all manifest entries remain inside the approved root. Require regular local
   files, safe ancestors and the expected digests. Verify immediately before a launch can start.
2. Build the process filename and fixed argv from the verified version. For a script-based
   tool, derive the interpreter argument from the verified script path. Caller parameters may
   fill only approved data slots. A mutable script path, alternate interpreter mode or dynamic
   code-loading option cannot satisfy the approved launch contract. The implemented interpreter
   profile is a fixed script launched under a pinned Linux dash executable.
3. Reuse the existing process factory and argument validation. Keep the approved working
   directory and disposable scratch separate. Restrict tool-specific loader inputs to the
   reviewed files and trusted host environment for the supported launch form. Existing container
   and host isolation policies continue to gate execution independently of native pinning.
4. Carry the artifact version into structured audit and the operation checkpoint. A resume that
   encounters a different version for an earlier operation should require explicit recovery.
   It must not create a new operation ID and replay a side-effecting tool silently.

The protected version directory and append-only publication protocol close the path replacement
window within the declared trust boundary. SHA-256 detects content drift. A writable generation,
an unverifiable permission boundary, an unknown manifest or a digest mismatch refuses launch.
Owner-account environments share their OS identity with other owner processes. Describe any
pinning there as a version check unless a separate enforceable storage boundary is present.

### Upgrade and acceptance

Stage migration by inventorying legacy `EXECUTABLE` aliases and recording which require an
explicit admin approval. Enforced pinning refuses unversioned entries. Back up catalog state
before activation, and exercise an approved upgrade and rollback on the supported host profile.
Keep this change in the breaking-change ledger when the enforced contract is implemented.

Use a real harmless fixture on Windows and Linux under a restricted runtime identity. Prove
that a changed executable, fixed script or interpreter fails before process start and that the
runtime cannot replace the version directory or artifact. Synchronize a rotation test between
verification and launch to show that the selected version stays in use while new invocations
use the activated version. Cover a failed update, approved rollback, checkpoint recovery and
scratch cleanup without removing versioned assets. Run the existing tool-hardening tests
alongside the real process fixtures. Limit the certification claim to the tested launch forms,
host identities and platforms. [Existing tool certification](governed-custom-tool-runner-certification.md)

## Implementation order

| Work | First reviewable result | Initial estimate |
| :--- | :--- | :--- |
| XT07 identity and catalog | One verified Orchestrator principal projection, versioned catalog records, admin/use separation and a scoped allow/deny fixture. | 1–2 engineering days |
| XT07 host evidence | Grant mutation and revocation, scheduled/process identity, service scope and owner-account fixtures. | 2–3 engineering days after the first result |
| XT08 Windows installed acceptance | Use the restricted service profile to publish and execute two direct PE generations through the installed Orchestrator. Prove rotation, rollback, and refusal after tampering. | [Verified for the candidate MSI](#qualify-the-installed-orchestrator) |
| XT08 fixed interpreter | The fixed dash script form has real process evidence on restricted Linux workers. | Linux implementation verified |

The first implementation should confirm where scheduled principal verification can run and name
the Windows and Linux runtime identities for the acceptance fixtures. If a supported deployment
cannot enforce read/execute-only access for its worker, keep native pinning unclaimed for that
deployment until the launch boundary is established.
