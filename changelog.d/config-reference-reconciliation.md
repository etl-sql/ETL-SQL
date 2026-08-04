### Fixed

- **Four settings added this release were missing from the configuration reference** — the document
  an operator actually opens when configuring a deployment. They existed in guides and architecture
  prose, which is not where anyone looks to set a value:

  - `Studio.RequireApprovalToPublish` — the draft → review → publish workflow
  - `SourceControl.ProtectedBranches` — branches a Portal commit may not reach unreviewed
  - `DataQuality.AllowConnectionPreview` — the quarantine row-preview kill switch
  - `ReportApprove` — the tenth Studio capability, absent from the capability list operators copy

### Added

- `EveryStudioCapability_AppearsInTheConfigurationReference` guards that last class of drift.
  Capabilities are granted by typing their name into `Portal:Studio:RoleCapabilities`, and the
  filter rejects an unknown name rather than storing a typo — so a capability missing from the
  reference is one nobody can grant deliberately, and nothing anywhere reports it.
