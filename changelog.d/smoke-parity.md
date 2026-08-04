### Added

- `scripts/Invoke-SmokeParity.ps1` — runs the same acceptance profile against a locally-hosted
  Portal and the production container image, then **compares the two check by check**.

  Parity is a comparison, not two independent green runs. A container run that quietly skips checks
  the local run performed would otherwise report success while proving less, and that is invisible
  in any output which only says "passed". So both sides emit per-check JSON, and any check present
  in one and absent from the other — or with a different outcome — is a parity failure even when
  both runs exit zero.

  Both targets get identical configuration and a bind-mounted script root, so a difference in the
  results is a difference in the product rather than in the harness. The local side is pinned to
  `ASPNETCORE_ENVIRONMENT=Production`, because `appsettings.Development.json` overrides environment
  variables and would otherwise have the two sides reading different configuration.

- `Invoke-AcceptanceProfile.ps1` gained `-ResultsPath`, emitting every check and its outcome —
  including **skips** — as JSON. Recording skips is what lets a comparison notice that one target
  checked less, which is the failure mode the parity run exists to catch.

### Documentation

- Publishing a report by script path requires `Portal:Studio:Mode=SourceControlled` **and** the
  `ReportPublish` capability; `RequireStudioCapability` answers 404 in any other mode. Without those
  settings the acceptance profile silently seeds no report, three checks vanish, and the run still
  exits 0 — documented, because a green run that checked less is the most misleading outcome
  available.
