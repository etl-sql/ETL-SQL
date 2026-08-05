### Added

- **`EngineSubsystemCoverageTests` — a guard for the failure mode architecture documentation
  actually has: omission.** A wrong type name is caught the moment somebody follows it; a subsystem
  nobody wrote down is invisible. `Engine.md` described the external spill engines 69 times and
  data-quality rules, columnar plans, row-level security and adaptive execution zero times, for
  three releases, and nothing reported it.

  It inventories every code-bearing directory under `ETL-SQL.Engine` and `ETL-SQL.Core` and asserts
  set equality against a declared inventory. A new subsystem fails the build until someone says
  which page documents it, or records why none is needed. Where coverage is claimed, the named page
  must still contain a marker for it — so a page dropping a subsystem is caught too.

  **Deliberately not a text search.** Matching directory names against the prose was tried and is
  useless in both directions: `Data`, `Common` and `Services` match incidental English everywhere,
  while `Planning` reads as undocumented even though its types are described by name. The test does
  not infer coverage; it forces a decision.

  It found two undocumented subsystems while being written, now pinned by set equality so they
  cannot grow quietly: `Core/Observability` (the correlation and trace tags every log scope and
  audit record is keyed on) and `Core/Storage` (`IArtifactStorage`, the seam every host writes
  artifacts through and the thing HA requires to be shared).

  Known gaps are recorded rather than failed. Turning existing debt red only invites weakening the
  inventory to get green, and an inventory that launders omissions into approvals is worse than
  having none.

### Fixed

- **`Engine.md` now covers adaptive execution, and says the thing that matters about it.** Nine
  files under `Core/Adaptive` and no architecture page mentioned them. The accurate statement is
  narrower than their presence suggests: `AdaptiveExecutionController` computes bounded setpoint
  advice and `Evaluator` holds an advisor, but **no execution pipeline reads it**, so the subsystem
  records what it would do without changing how anything runs.

- **`AdaptiveExecutionController.md` said "DRAFT — no implementation yet"** while Slice A was
  implemented and wired into the evaluator. Corrected against the source, including the part still
  outstanding: pipelines opting in at safe boundaries.
