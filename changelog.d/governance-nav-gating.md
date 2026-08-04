### Fixed

- **Two Governance views were offered to roles that cannot open them.** *Overview* needs
  `GovernanceRead` and the *Quarantine Queue* needs `DataQualityStewardAccess`, but both were shown
  to every signed-in user. Only *Audit Evidence* was gated. Both are now revealed to the roles their
  APIs accept, matching the pattern Audit already used.

  The Governance section itself stays visible to everyone, and that is deliberate: *Lineage Search*
  and *Stewardship* are open to any authenticated user, and tracing where a number came from is
  exactly what a report consumer needs them for.

- **Clicking Governance routed everyone to the quarantine queue** — so a report consumer's first
  click on the section landed them on the one view they are refused. The landing view is now the
  first one the user can actually use, resolved in a single place so the top-level link, the bare
  `#governance` hash, and the sidebar cannot disagree.

- **Deep links to a Governance view a role cannot use now redirect rather than opening.** Hiding a
  navigation entry does nothing for someone who was sent a link, which is how these URLs mostly get
  reached.
