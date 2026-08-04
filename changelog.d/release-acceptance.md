### Changed

- **The pre-release gate now states what it actually verifies.** Three phases in
  `Test-PreRelease.ps1` described their lanes in the abstract while the lanes had grown well past
  the description — a gate whose coverage you have to infer from test filters is one nobody can
  review.

  - The **browser lane** phase now names the critical journey, the four non-Admin role journeys,
    the accessibility and responsive checks at 1440px and 390px, the accessibility-tree snapshots,
    and the sandbox story mounts.
  - The **Portal lane** phase now names the release-acceptance journeys it already carried: the
    role/permission authorization matrix, departmental environment isolation across two
    deployments, policy authority and distribution, module gating, Studio capabilities, and the
    browser API contract.
  - A new **local/container smoke parity** phase runs under `-IncludeDockerIntegration`, comparing
    the two targets check by check rather than accepting two green runs.
