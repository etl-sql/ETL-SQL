### Added

- **`Engine.md` now documents artifact storage** — the seam every host writes scripts, snapshots,
  datasets, maps and key material through, and which had appeared in no architecture page at all.
  Covers the `ArtifactArea` set, the providers, and the two decorators that carry the guarantees:

  - `Keys` is not just another area. Providers treat it as secret — owner-only permissions on write
    and no local-copy leasing — so a caller cannot obtain key material on disk the way it can a
    snapshot.
  - `GuardedArtifactStorage` enforces the deployment's security guardrails at the single storage
    boundary, reusing `SecurityService`'s extension lists rather than keeping a second copy.
  - `FencedArtifactStorage` applies database-backed **write-epoch fencing**. On shared storage
    without native fencing, a writer must claim the artifact's epoch through `IWriteEpochStore`
    before a create, replace, move destination or delete; an older token is refused and *the byte
    write never happens*. This is what stops a node that has lost its lease but not yet noticed from
    overwriting newer work — and it is why HA needs artifact roots genuinely **shared** rather than
    merely identical, since two nodes writing to separate directories never contend for the same
    epoch.

- **`Engine.md` now documents the observability conventions.** `ObservabilityConventions` holds the
  shared, deliberately low-cardinality tag and metric names. The reason they exist is the part worth
  writing down: they keep free-form names, file paths, SQL text, parameter values and connection
  strings *out* of telemetry. That is a cost control — high-cardinality labels are what make a
  metrics backend expensive — and a disclosure control, because a label travels wherever telemetry
  goes and is not covered by the redaction applied to logs and support bundles.

  Both gaps were found by `EngineSubsystemCoverageTests` while it was being written, and both are
  closed by writing the pages rather than by relaxing the inventory. Its known-gap list is now
  empty; the test pinning it stays, so a future gap has to be added on purpose.
