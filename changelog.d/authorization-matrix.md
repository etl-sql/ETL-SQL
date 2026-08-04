### Added

- `AuthorizationMatrixTests` — the Portal's authorization rules asserted as data, one grant × one
  operation at a time, so a privilege change cannot ship by accident: a widened grant fails a
  `denied` row and a narrowed one fails an `allowed` row. The negative rows are the point, since a
  suite that only proves people can do things proves nothing about what stops them.

  Writing it surfaced two properties of the model that were previously discoverable only by reading
  about forty enum comparisons plus scattered `[Authorize(Roles=…)]` attributes:

  - **Authorization is two-dimensional.** A *role* decides which class of operation you may perform
    at all; an *ACL* decides which resources you may perform it on. The axes are not
    interchangeable, and conflating them is how a grant comes to mean more than intended.
  - **`Manage` on a folder is authority over the reports in it, not over the folder itself.**
    Reading or re-granting a folder's ACL, creating a subfolder, and deleting a folder are Admin-role
    acts. Without that split the highest ACL grant would be self-propagating: whoever held it could
    hand it out, so the set of people with access could only ever grow.

  The report-scoped case is driven through the real path — request access, admin approves — because
  there is no endpoint that grants a report ACL directly, and a shortcut through the database would
  prove the ACL works while proving nothing about how one comes to exist.
