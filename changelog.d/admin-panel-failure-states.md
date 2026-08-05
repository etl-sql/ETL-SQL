### Fixed

- **The folder permissions panel could show one folder's grants under another folder's name.**
  Opening a folder's permissions sets the heading immediately and then loads the ACL; the table was
  only written on success, so a failed load left the previous folder's rows in place. The panel was
  not blank, it was confidently wrong, and wrong about access control specifically — an
  administrator could read another folder's grants as this one's, and the Revoke buttons still
  carried the other folder's group ids while the revoke call sent this folder's.

- **A failed group-membership read rendered as a group with no members.** "Nobody is in this group"
  and "we could not find out" lead an administrator to opposite actions, one of which is deleting
  the group or granting its access elsewhere.

  Both panels now clear before the request and render the shared `failedState` after one — the
  four-state vocabulary exists precisely so a failure is never shown as an emptiness.

### Added

- `AdminPanelFailureStateTests` drives both panels with only their own request failing, which is
  the shape the real failure takes: one call rejected, the rest of the page fine.

- `BrowserRouteReachabilityTests` asserts every `/api/...` path the Portal's own JavaScript calls
  resolves to a route the Portal serves. The client turns a rejected request into a caught error,
  which renders as "nothing to show" or "temporarily unavailable" — so a renamed or mistyped route
  produces no symptom a reviewer would notice. It found nothing today; it is a guard, not a
  discovery, and its scope is deliberately narrow: existence only, not authorization and not the
  response shape.
