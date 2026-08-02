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
./scripts/Test-DeploymentProfileCertification.ps1 -Transition Upgrade
./scripts/Test-DeploymentProfileCertification.ps1 -Transition All
```

The Team-to-Enterprise lane includes PostgreSQL migration proof and therefore requires the same
Docker/provider prerequisites as its focused suite. Missing prerequisites are a failed certification,
not a pass or an inferred claim.

## Evidence contract

Each run creates a timestamped directory below
`certification-results/deployment-profiles/` unless `-OutputRoot` is supplied. The directory contains:

- `certification.json` — schema version, commit, dirty state and paths, selected lanes, host metadata,
  exact commands, exit codes, and phase results.
- `certification.md` — a compact operator review of the same result.
- one log per phase — the complete focused-suite output used by that phase.

Evidence is commit-bound. A dirty run remains useful while developing, but it is not current release
evidence. Release claims require a passing run for the exact clean commit being released. Keep the
JSON, Markdown, and referenced logs together; moving only the summary destroys the audit trail.

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
