### Added

- **Draft → review → publish for report scripts**, opt-in behind
  `Portal:Studio:RequireApprovalToPublish` (default **off**). Saving a script previously wrote
  straight over the running report, so "save" and "publish" were the same act and review could only
  ever happen after the fact. A draft is what makes the gap between authoring and publishing
  representable.

  The proposed script lives in the database rather than the artifact store, deliberately: a draft is
  not yet a script — nothing should execute it, serve it, or list it beside real ones — and keeping
  it out of the script directory makes that structural instead of a naming convention everyone has
  to remember.

  **Separation of duties is absolute.** An author can never approve their own draft, whatever
  capabilities or roles they hold, **including Admin**. A four-eyes control that the most privileged
  account can bypass fails exactly when it is needed, because the account that gets compromised or
  leaned on is the privileged one.

  Three further rules follow from an approval being about *content*, not about a draft id:

  - Editing a draft revokes any approval **and** any review in progress, returning it to the author.
    Otherwise a trivial change could be approved and the body swapped afterwards, or a reviewer could
    have content change mid-read — either way a reviewer's name would end up on something they never
    saw.
  - Every decision records the script hash it was made against, so "was this reviewed?" is answerable
    for the version that actually shipped.
  - Publishing is refused when the live script has moved past the draft's base. The approval was for
    a change against a version that is no longer there, and publishing anyway would silently discard
    whatever landed in between.

  Every mutation takes `If-Match` with the draft's version, and the decision trail is append-only —
  a reviewer who approved and later changed their mind is a different history from one who only ever
  rejected, and that distinction is what a post-incident review is looking for.

- A `ReportApprove` Studio capability, separate from `ReportPublish` so that reviewing a change and
  shipping it can be given to different people.
