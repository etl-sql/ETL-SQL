# Deployment-profile certification

Deployment-profile certification composes focused test suites into operator-readable proof for the
Solo, Team, Enterprise, and SaaS contracts and their supported transitions. It does not replace the
focused suites. It records exactly which command supplied each claim and fails the selected lane when
any required phase fails or is skipped.

## Run a profile lane

Run from the repository root with PowerShell 7 or later:

```powershell
./scripts/Test-DeploymentProfileCertification.ps1 -Profile Solo
./scripts/Test-DeploymentProfileCertification.ps1 -Profile Team
./scripts/Test-DeploymentProfileCertification.ps1 -Profile Enterprise
./scripts/Test-DeploymentProfileCertification.ps1 -Profile SaaS
./scripts/Test-DeploymentProfileCertification.ps1 -Profile All
```

Use `-NoBuild` only after the selected test projects have been built at the requested
`-Configuration`. Use `-Explain` to inspect the exact phases without executing them.

## Run a transition or upgrade lane

```powershell
./scripts/Test-DeploymentProfileCertification.ps1 -Transition SoloToTeam
./scripts/Test-DeploymentProfileCertification.ps1 -Transition TeamToEnterprise
./scripts/Test-DeploymentProfileCertification.ps1 -Transition EnterpriseToSaaS
./scripts/Test-DeploymentProfileCertification.ps1 -Transition SoloToSaaS
./scripts/Test-DeploymentProfileCertification.ps1 -Transition SaaSToEnterpriseExit
./scripts/Test-DeploymentProfileCertification.ps1 -Transition Upgrade
./scripts/Test-DeploymentProfileCertification.ps1 -Transition All
```

The Team-to-Enterprise lane includes PostgreSQL migration proof and therefore requires the same
Docker/provider prerequisites as its focused suite. Missing prerequisites are a failed certification,
not a pass or an inferred claim.

`SaaSToEnterpriseExit` is the customer exit journey and is the only lane that runs *backward*. It is
not a promotion: promotion preflight refuses any backward move (`DP001`) and directs the operator to
an explicit export/restore workflow, which is the portable tenant bundle. The lane certifies that
workflow — a signed, tenant-encrypted bundle that verifies and decrypts with no contact with the
source operator, and a target preflight that states every binding the target owes before anything
mutates. Do not satisfy it by relaxing `DP001`.

## Enterprise hosted prerequisites

The Enterprise profile lane is also the gate a hosted (SaaS) deployment builds on, so it proves eight
prerequisites in **one run against one commit**:

| Prerequisite | What the lane proves |
| :--- | :--- |
| `verifiable-caller-identity` | Federated OIDC identity and signed Orchestrator assertions carry a verifiable principal; an unsigned actor header carries no authority, and the Portal's own outbound calls carry a signed caller rather than the service key alone. |
| `per-object-authorization` | Reaching an Orchestrator confers no authority over another principal's objects, `CREATE OR ALTER` cannot take over a shared name, every mutation verb leaves an audit record naming the acting principal, and a narrowly scoped service token is narrow at both doors. |
| `shared-state-and-artifact-providers` | Portal and Orchestrator state resolve against shared PostgreSQL across processes; artifact storage honours its guarded contract. |
| `scoped-secret-and-policy-authority` | Typed organization policy, the encrypted audited catalog secret store, and signed policy distribution guard execution and publishing. |
| `durable-audit` | The remote audit outbox retains, redacts, and recovers mutation records instead of dropping them when the collector is unreachable. |
| `high-availability` | Database leases and write epochs fence stale owners. |
| `backup-and-restore` | Portal and engine backup/restore round-trip to a usable state and validate before claiming success. |
| `upgrade-and-promotion-evidence` | The Enterprise profile completes an N→N+1 lifecycle with a scheduler-safe rollback point, and promotion preserves bindings and ownership. |

Because it exercises shared PostgreSQL providers, **the Enterprise profile lane requires Docker**, as
the Team-to-Enterprise lane already did.

`certification.json` records a `hostedPrerequisites` array and `certification.md` renders it as a
table. A prerequisite whose phases never ran — the runner stops at the first failure — is reported by
name as unproven and fails the lane. This matters because the alternative was assembling the hosted
claim by correlating the Enterprise, Upgrade, and Team-to-Enterprise lanes by hand, which is the
inferred claim this framework refuses everywhere else.

The joined table is a prerequisite gate, not a SaaS isolation claim. It says the foundation a hosted
deployment stands on holds for this commit; it says nothing about tenant isolation in either SaaS
topology, which each domain must certify with its own topology evidence.

For a release candidate, run both aggregate lanes against the clean candidate commit:

```powershell
./scripts/Test-DeploymentProfileCertification.ps1 -Profile All -ReleaseVersion 0.18.0
./scripts/Test-DeploymentProfileCertification.ps1 -Transition All -ReleaseVersion 0.18.0
```

`-ReleaseVersion` changes the default output root to
`artifacts/release-evidence/<version>/deployment-profiles/` and maintains stable
`claims-index.json` and `claims-index.md` files there. The index keeps profile and transition rows
separate and links each row to its timestamped evidence bundle.

## Evidence contract

Each run creates a timestamped directory below
`certification-results/deployment-profiles/` unless `-OutputRoot` is supplied. The directory contains:

- `certification.json` — schema version, commit, dirty state and paths, selected lanes, host metadata,
  exact commands, exit codes, topology claims, journey-fixture hash, mapping decisions, continuity
  identifiers, negative proof, concrete lifecycle scenarios, and phase results.
- `certification.md` — a compact operator review of the same result.
- one log per phase — the complete focused-suite output used by that phase.
- `scenario-evidence/*.json` — concrete artifact hashes, import counts, continuity counts, negative
  isolation results, and rollback outcomes emitted by transition, upgrade, and Managed Dedicated
  onboarding tests.

Evidence is commit-bound. A dirty run remains useful while developing, but it is not current release
evidence: `releaseEligible` remains false even when every phase passes. Release claims require a
passing run for the exact clean commit being released. Keep the
JSON, Markdown, and referenced logs together; moving only the summary destroys the audit trail.

SaaS evidence is topology-specific. The current SaaS lane certifies **Managed Dedicated** only and
records Shared SaaS as `NotCertified` in the run and release-claims index. No Enterprise or Managed
Dedicated result may be reused as Shared SaaS evidence.

The journey fixture at `tests/fixtures/deployment-profile-journeys.json` defines the required positive
and negative proof, portable state, host-owned state, and continuity identifiers. Its contract test is
the first phase of every certification lane.

## Failure behavior

The runner stops after the first failed phase, writes a failed evidence bundle, and exits with code 1.
It never turns missing Docker, unavailable providers, skipped proof, or target collisions into a pass.
Fix the focused failure, rerun the same lane, and retain only evidence that matches the intended claim.

## References

- [Deployment profile strategy](../../architecture/roadmaps/Deployment_Profile_Strategy.md)
- [Deployment profile transitions](profile-transitions.md)
- [Deployment promotion](deployment-promotion.md)
- [Release checklist](../../releases/release-checklist.md)
