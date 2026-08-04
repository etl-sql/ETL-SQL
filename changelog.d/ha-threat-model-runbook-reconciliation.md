### Fixed

- **Half the `/healthz` readiness finding codes were undocumented.**
  `PortalTopologyReadinessService` emits six; the HA certification document listed three "such as"
  examples. The three missing ones included `ha-requires-session-affinity` and
  `ha-requires-orchestrator-postgres` — both of which hold a node out of load-balancer rotation. All
  six are now documented in a table with cause and remedy, because a finding code is what a 503 says
  about itself and the string an operator greps for mid-incident.

- **`Portal:Topology:*` was absent from the configuration reference entirely** — five settings that
  decide whether `/healthz` returns 200, missing from the document an operator opens to configure a
  deployment. The same class of drift the previous reconciliation found in the Studio settings.

- **`ExpectedMode: Auto` can hold a working node out of rotation, and nothing said so.** `Auto`
  infers `HighAvailability` from PostgreSQL *or* a configured `Portal:Storage:KeyRingPath`, and never
  infers `Departmental`. So a single-node SQLite Portal that merely moved its key ring off the
  default path is classified HA, `RequirePostgresForHa` applies, and `/healthz` returns 503 with
  `ha-requires-portal-postgres` — a node that is otherwise working, that the load balancer stops
  routing to. The inference is right (a shared key ring is a multi-node signal) but the contract it
  turns on is strict, and a departmental deployment on PostgreSQL is the common case. Now stated in
  the HA certification document, the configuration reference, the HA administration guide, and as a
  **Required** step in the production-readiness checklist.

### Added

- **An HA topology diagram** separating what ETL-SQL coordinates from what the operator's
  infrastructure provides. During an incident a node returning 503 is usually reporting a failure
  from the other side of that line, and the document previously described the boundary only in
  prose.

- **`HaAndSecurityDocReconciliationTests`** guards the claims that can be checked against source:
  every emitted finding code and `checks` key is documented; every topology and load-balancer
  setting appears in the configuration reference; every test named in the Automated Coverage Map
  still exists; and every `ha-soak` subcommand a runbook tells an operator to type is defined by the
  CLI. A coverage map naming a deleted test claims a certification nobody performed, and a runbook
  step is followed by typing what it says.

- The `Auto`-mode trap is asserted, not argued:
  `AutoMode_TreatsAConfiguredKeyRingAsHighAvailability_AndFailsClosedWithoutPostgres` drives the real
  `/healthz` endpoint through the case.

- **The read-only fleet boundary is now enforced.** The Enterprise Security Review Packet approves
  fleet aggregation as status polling and explicitly does not approve remote mutation;
  `FleetAggregation_ExposesNoMutatingRoutes` fails the build if a mutating route is added. A trust
  boundary stated only in a document lasts until the first convenient `POST`.

- The security review packet's scope and trust-boundary table now cover the Portal authority
  surfaces this release added — Studio capabilities, the draft review path and protected branches,
  and the disclosure surfaces (support bundle, configuration export, access simulator, posture
  endpoints) — each with the evidence that constrains it, plus the review decisions they require.
