### Added

- **Protected branches for Portal-originated commits.** `Portal:SourceControl:ProtectedBranches`
  (empty by default; exact names, or a prefix when the pattern ends in `*`) names branches a commit
  may not land on without an approved draft behind it.

  This is what the draft-approval workflow is *for*. Protecting a branch without a review path only
  blocks people; providing a review path without protecting anything only asks nicely. Together they
  mean a change reaching a protected branch has been read by someone other than its author.

  The reviewer is written into a `Reviewed-by:` commit trailer alongside the script hash, so the
  review outlives the Portal's database — someone auditing the branch a year later reads it from
  `git log` rather than needing the Portal to answer "who approved this?".

  Three details that decide whether the protection is real:

  - The branch is read **inside the repository lock**, immediately before committing. Checking it
    outside the lock protects nothing, because it can change in between.
  - Approval is matched on the **published script hash**, not on recency. A draft that was approved
    but never published cannot lend its approval to whatever happens to be on disk now.
  - An unknown branch — detached HEAD, or git unavailable — is treated as unprotected. Failing open
    here is deliberate and narrow: the commit still passes every other check, and treating "I could
    not tell" as "protected" would turn a diagnostic gap into an outage.

  Refused commits are audited as `COMMIT_REPORT_SCRIPT_DENIED`. An attempt to put an unreviewed
  change on a protected branch is exactly the event an operator wants to see, and a bare 409 would
  leave no trace of it.
