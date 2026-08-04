### Fixed

- **`docs/architecture/Portal.md` said three Identity roles were seeded. There are eight** — five of
  them security-relevant, including every governance role. An architecture document that is
  confidently wrong is worse than a missing one: a missing document sends people to the code, and a
  wrong one stops them.

  Also corrected there: the authorization model is **two independent axes** (a role decides which
  class of operation, an ACL decides which resources); folder `Manage` is authority over the reports
  in a folder, **not** over the folder itself; and `FolderPermission` must never be compared
  ordinally, because `Author` is stored above `Manage`.

- **Eleven API areas were entirely undocumented** — branding, OIDC, service accounts and tokens,
  both policy-authority surfaces, configuration promotion, Studio, designer, docs, and fleet — along
  with the governance, report-draft and data-quality endpoints added this release, and three
  persisted entities.

### Added

- `ArchitectureDocReconciliationTests` — checks the architecture document's mechanically checkable
  claims against source: every seeded role, every persisted entity, every named authorization
  policy, and every API area is documented.

  Deliberately limited to claims that can be verified. Prose about intent cannot be checked from
  source, and a test that pretended to would either be vacuous or would block every honest
  rewording. It found substantially more drift than a reading pass had.
