### Added

- **A four-path deployment-profile review for the release.** `Deployment_Profile_Standards.md` has
  prescribed one since it was written — "a release claim must name the profile and transition it
  actually proves" — and no release had produced one. `v0.18.0-deployment-profile-review.md` is
  that review: driven from the release's changelog fragments rather than from memory, grouped into
  six capability areas, each stating how Solo, Team, Enterprise and SaaS accomplish it.

  The summary is the finding. **v0.18.0 is a Portal and Enterprise release**, and most of what it
  added has no Solo form because Solo has no Portal. That is a legitimate answer on one condition —
  the underlying evidence must stay reachable without the Portal — and the release meets it: every
  governance and quality surface it added reads `eng.*`, which the CLI, Report Player and
  Orchestrator serve from the same code. The review states where that holds and where it does not,
  rather than colouring cells green.

  **No matrix cell moved to Green.** The release strengthens evidence behind existing Green cells
  and adds acceptance lanes that make them re-testable. The SaaS column is unchanged and remains
  Red for every concern touched; the Enterprise happy path is not evidence for a mutually untrusted
  tenant boundary.

  Three things it records that were not written down anywhere: the Portal governance dashboard and
  `eng.stewardship_score` use different scoring models and will not agree — compare them knowingly;
  recovery custody stays on the host in every profile, however large; and `Portal:Topology:ExpectedMode`
  defaults to `Auto`, which classifies a Team deployment on PostgreSQL as HA and holds it out of
  load-balancer rotation until told otherwise.
