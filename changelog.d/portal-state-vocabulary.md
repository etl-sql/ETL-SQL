### Added

- `js/portal-states.js` — a shared vocabulary for the four states every Portal surface has to
  render: **loading**, **denied**, **failed**, **empty**, plus `statusChip`.

  They look almost identical on screen — a mostly blank panel — which is exactly why they get
  conflated, and why the difference has to be carried by wording rather than layout. A user who
  cannot tell "you may not see this" from "the service is down" from "there is nothing here" reads
  all three as the last, because it is the only one that needs no action from them. Each state emits
  a `data-portal-state` marker so a test can assert *which* state a surface reached rather than
  inferring it from whatever text happens to be present.

  Extracted from the governance module's pattern rather than invented, and guarded by
  `PortalStateVocabularyTests`.

### Fixed

- **The connection catalog showed the same message for a 403 and an unreachable service.** "Could
  not load connections" reads as a fault to report, when the answer may simply be that this account
  may not see the catalog. The two are now distinct, and the failure case offers a retry.

- **Escaping in the shared states happened two frames from the interpolation it protected.** Caller
  values were escaped by an inner helper, which works but is invisible at the call site and would
  double-escape anything a caller sensibly escaped itself. The rule is now simply: escape at the
  point of use. `PortalStateVocabularyTests` fails on any caller-supplied value interpolated raw —
  one unescaped interpolation would be an injection point on every surface that adopts the
  vocabulary, which is the cost of sharing it.
