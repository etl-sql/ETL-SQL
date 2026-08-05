### Fixed

- **Three architecture docs named interfaces that no longer exist.** `Engine.md` listed
  `ICryptoService` and `ISecurityService` for crypto and security — neither is in the tree — and
  `LanguageServer.md` told contributors to implement `ICodeActionHandler`, which this server does
  not implement. Corrected to the types that are actually there: `CryptoUtils`, `SecretRedactor`,
  `ISecretLifecycleProvider` and `IEnterpriseEnrollmentProtector`; and the four OmniSharp handler
  interfaces the language server really uses.

### Added

- **A mechanical staleness audit of `docs/architecture`, recorded in `TODO.md`.** Every `src/…`
  path and backticked type name was resolved against the tree rather than read for plausibility.

  The wrong-statement rate turned out to be low — of ~16 flagged type references, all but the three
  above were false positives (role names, framework types, TypeScript classes, test-only types), and
  every cited source path resolves.

  **The real staleness is omission, and it is concentrated in `Engine.md`**, which documents the
  v0.10-era engine accurately and has not grown with it. It mentions the external spill engines 69
  times and the following zero times: data-quality rules, the `Columnar*Plan` fast-path family,
  row-level security, and `SECRET:`/organization-policy enforcement. All four were confirmed
  engine-level, not Portal-only.

  That matters more than a stale type name: data-quality rules **pin execution to the local row
  pipeline** — the columnar fast-path gates deliberately exclude rule-carrying statements — so a
  reader using `Engine.md` to understand dispatch and fast paths cannot see a constraint governing
  both.

  `Orchestrator.md`, `Lineage.md`, `Connectors.md`, `Reporting.md` and `Portal.md` were checked and
  need no action.
