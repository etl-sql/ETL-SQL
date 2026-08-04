### Added

- **Approver coverage completes the authorization matrix.** Approving is a *capability* rather than
  a role, so its rows live with the workflow they govern: approving requires `ReportApprove`,
  asserted **both ways** — the positive row alone would prove approval works without proving
  anything stops it — and an approver cannot publish, because reviewing a change and shipping it are
  separate authorities an organization needs to be able to give to different people.

### Fixed

- **Three dialogs were announced as just "dialog"** — the governance quality-trend modal and two in
  the data-quality queue (trend and row editor). Each already had a visible `<h2>` title; none was
  linked to it, so a screen-reader user was told a dialog opened and nothing about which job or
  target it concerned. All three now use `aria-labelledby`.

  Caught by `PortalDialogAccessibilityTests` on modals added after that guard was written, which is
  the case it exists for.
