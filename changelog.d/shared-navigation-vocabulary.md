### Fixed

- **Studio was offered to every signed-in user, including roles holding no Studio capability.**
  Pages revealed the entry whenever the Studio capability *probe* succeeded — but that probe was
  deliberately opened to every authenticated user so that asking "what may I do in Studio?" would
  stop being an error for the roles that may do nothing. The probe answering is not the answer being
  yes. A Viewer, DataSteward or OrchestratorManager saw a Studio link that only leads to a 403.
  Found by a red test across all three roles before anything was changed.

- **The Docs link was offered on deployments where `/docs.html` returns 404.** Whether Documentation
  is enabled is a server fact with no token claim behind it, so no amount of care in the page could
  have got this right. Two pages did not even carry the `docsNav` hook the others gated on.

- **Governance nav gating in `docs.html` was one role wider than every other page.** It admitted a
  role named `Orchestrator`, which does not exist in the Portal's role set — a copy of the rule that
  had drifted and that nobody could see without diffing six pages against each other.

- **Accessibility-tree snapshot baselines were never committed.** A blanket `*.txt` in `.gitignore`
  swallowed `tests/ETL-SQL.Portal.BrowserTests/Snapshots/*.snapshot.txt`, so the baselines existed
  only on whichever machine last generated them. Updating one is supposed to be a review decision
  visible in a diff; it was invisible. That is how the governance sidebar baseline went stale and
  stayed stale.

- **Three browser assertions left behind by the governance sidebar rework.** The sidebar snapshot
  still described the old menu, `RoleJourneyTests` still required a `Stewardship` entry that was
  deliberately removed, and a dashboard test still clicked an in-page tab strip that no longer
  renders — so it had been timing out rather than testing anything. All three now match the shipped
  design.

### Added

- **`GET /api/portal/navigation`** — which top-level entry points to offer *this* caller, computed
  once on the server from roles, module state and Studio capabilities. Six pages used to derive this
  from JWT claims in five different spellings of the same decision, and the two destinations above
  cannot be derived from a claim at all.

- **`js/portal-nav.js`** applies the answer and never computes one. A client-side guess is what it
  replaces, and a wrong guess that *shows* an entry is worse than one briefly missing, so there is
  deliberately no fallback rule. It stamps `data-nav-applied` when the answer has been applied —
  "hidden because you may not have it" and "not decided yet" are identical in the DOM, so without
  the marker an absence check races the fetch and goes green for the wrong reason.

- **`PortalNavigationVocabularyTests`** keeps it one vocabulary: no page may set a server-decided
  destination's visibility itself, every page carrying the top bar applies the shared answer, and
  every destination the server decides has somewhere to land on every page. Copy-paste is the
  natural thing to do when adding a page, so the invariant is enforced rather than remembered.

- **`NavigationVisibilityTests`** covers the rule server-side and in both directions — including
  that holding *some* Studio capability is not the same as holding `StudioAccess`, and that hiding
  Studio unconditionally would fail, since the negative assertion alone would accept exactly that.
