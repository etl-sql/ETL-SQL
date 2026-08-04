### Added

- An **`Author`** grant on folders and reports, for the person who maintains a report without
  administering the folder it lives in. An Author may rewrite a report's script, content and
  metadata, and run it. They may **not** move it to another folder, delete it, publish a new report
  into the folder, create share links or embed tokens, or change any ACL. Moving a report changes
  what two folders contain and deleting one changes what a folder contains — neither is an act on
  the report's content, which is the only thing an Author was given authority over.

  It is available wherever `Read`/`Execute`/`Manage` already were, ordered between Execute and
  Manage in the pickers.

### Fixed

- **`FolderPermission` comparisons no longer depend on the enum's numeric order.** The values are
  persisted as integers in every ACL row, so `Author` had to be appended as `3` rather than inserted
  in its rightful place between `Execute` (1) and `Manage` (2) — inserting it would have renumbered
  `Manage` and silently reinterpreted every grant already in force, with no migration able to detect
  it because the rows stay valid and merely mean something else.

  That left declaration order lying about authority. Roughly forty `permission >= FolderPermission.Manage`
  comparisons would each have granted `Author` everything `Manage` has, and four integer `Max`
  operations picking the strongest of several grants would have chosen `Author` over `Manage`,
  *downgrading* anyone who held both.

  `FolderPermissions.Rank()` now defines the ladder (Read < Execute < Author < Manage) independently
  of the stored value; `AtLeast()` replaces every ordinal comparison and `Max()` every integer max.
  The conversion was done in two phases — behaviour-preserving first, verified against the full
  suite, then the deliberate grants one gate at a time — and `FolderPermissionOrderingTests` fails
  the build if any production file compares permissions ordinally again, because writing `>=` here
  is the natural thing to do and silently escalates.
