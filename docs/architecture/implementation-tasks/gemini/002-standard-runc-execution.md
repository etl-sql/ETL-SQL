# GEMINI-002 — Add Explicit Standard Docker Desktop Execution

> **Status: complete (2026-08-17).** Standard mode and its unit tests landed in `dfac9ac9`; the
> Docker Desktop integration evidence is `tests/ETL-SQL.Tests/Orchestration/DockerStandardSandboxLifecycleTests.cs`
> with its gate in `DockerSandboxEnvironment.cs`. Do not re-implement. Making a real workload run
> also required directing all writable container state into the assignment tmpfs and fixing
> `ProcessActor` for unmapped container uids; both are described in `TODO.md` domain 4.

## Dependency

Start only after GEMINI-001 is reviewed and its worker image contract is available.

## Objective

Allow the existing Docker OCI sandbox lifecycle to run real workloads on this development host's
registered `runc` runtime while emitting **Standard** isolation evidence only. Preserve the current
rule that Hardened and Dedicated work requires an allowlisted gVisor or Kata runtime.

This task exists to exercise real mounts, cancellation, cleanup, and residue behavior on Docker
Desktop. It must not make `runc` satisfy a hostile-tenant boundary or close the Shared/Dedicated
Hardened TODO cells.

## Read first

- `AGENTS.md`
- `TODO.md`, especially the certification-environment constraint in domain 4
- `src/ETL-SQL.Orchestrator/Execution/DockerSandboxExecutionProvider.cs`
- `src/ETL-SQL.Orchestrator/Execution/SandboxExecutionCoordinator.cs`
- `src/ETL-SQL.Orchestrator/Execution/SandboxExecutionHosting.cs`
- `src/ETL-SQL.Orchestrator/Execution/SandboxWorkloadPolicyResolver.cs`
- All `Sandbox*Tests.cs` and `DockerSandboxExecutionProviderTests.cs`

## Security invariants

These are acceptance conditions, not suggestions:

1. Runtime capability is server-owned. A job cannot choose `runc`, a physical runtime, a provider,
   or a higher evidence tier.
2. `runc`, `io.containerd.runc.v2`, `crun`, Windows process containers, and lookalike runtime names
   can emit at most `SandboxIsolationTier.Standard`.
3. A Standard provider must reject Hardened or Dedicated requests before `docker create`.
4. The existing `AddHardenedSandboxExecution` path must continue to reject Standard profiles and
   ordinary runtimes at startup.
5. A Hardened runtime must never be inferred from a substring such as `runsc-malicious`.
6. Dedicated placement still requires the fixed host tenant and fixed pool. Standard mode cannot
   advertise Dedicated.
7. Digest-pinned registry references remain required for Hardened/Dedicated. If local Docker image-ID
   support is necessary for Standard development, it must be a separately named, explicit option and
   must never be accepted by the Hardened registration path.
8. Existing read-only root, non-root user, no-network, capability drop, no-new-privileges, memory,
   PID, tmpfs, exact mount, label, teardown, and reconciliation controls remain unchanged.

## Implementation boundary

Prefer an additive registration entry point such as `AddStandardDockerSandboxExecution` with a
separate configuration section. Shared command-building code may be refactored, but do not weaken
or silently reinterpret `AddHardenedSandboxExecution`.

The provider evidence tier must come from the validated provider/runtime binding, not from
`request.RequiredIsolationTier`. A request asks for a minimum; the environment decides what it can
prove.

Keep the public configuration explicit. Operators should be able to tell from configuration and
startup logs that the host is Standard-only. Do not call the Standard section, mode, tests, or output
"hardened."

## Required tests

Add unit tests proving:

- Standard + registered `runc` prepares and emits Standard evidence.
- Standard + local image-ID mode works only when explicitly enabled.
- Hardened or Dedicated request on Standard fails before create.
- Ordinary runtime under the existing Hardened registration still fails startup.
- gVisor/Kata continue to emit Hardened (or Dedicated only with fixed placement).
- Runtime lookalikes fail.
- A caller-supplied job option cannot alter runtime, image, image identity, tier, mounts, pool, or
  host policy.
- Existing provider and hosting tests remain green.

Run at minimum:

```powershell
dotnet test tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj `
  --filter "FullyQualifiedName~DockerSandboxExecutionProviderTests|FullyQualifiedName~SandboxExecutionHostingTests|FullyQualifiedName~SandboxWorkloadPolicyResolverTests|FullyQualifiedName~SandboxExecutionCoordinatorTests" -m:1
```

If the exact hosting-test class name differs, use the actual related classes and report the filter.

## Docker Desktop integration evidence

Using the worker image from GEMINI-001, add an opt-in Docker integration test or script that:

- discovers the registered runtime rather than assuming one;
- requires `runc`/`io.containerd.runc.v2` and records Standard evidence;
- executes a harmless ETL-SQL script;
- cancels a long-running script and proves the container is absent afterward;
- proves the next assignment cannot read the prior assignment's input/output/scratch residue;
- confirms tenant A and tenant B receive different workspace/session/key paths;
- leaves no test containers or temporary roots after success;
- retains and reports state if runtime detachment cannot be proven.

The integration test must skip with a precise diagnostic when Docker is unavailable. It must fail,
not skip, when Docker is available but the asserted lifecycle is broken.

## Prohibited shortcuts

- Do not add `runc` to the Hardened runtime allowlist.
- Do not rename Standard evidence to Hardened.
- Do not accept a mutable `latest` tag as Hardened identity.
- Do not delete retained workspaces after ambiguous teardown.
- Do not change the TODO checkbox or capability matrix.

## Acceptance criteria

- Standard Docker Desktop execution works with real `runc`.
- Hardened/Dedicated fail closed on the same host.
- Unit and real-provider lifecycle tests pass.
- Output explicitly says `Docker Desktop / runc / Standard`.
- One focused commit, with unresolved external gVisor/Kata evidence called out in the handoff.
