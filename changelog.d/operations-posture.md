### Added

- Added recovery and host-identity posture at `GET /api/admin/operations/posture`, covering backup freshness, restore-drill evidence, and host enrolment consistency in one read-only view.

  Backup custody, the restore itself, and host enrolment all stay outside the running Portal — they own key material and an OS-protected bootstrap the Portal deliberately does not have. What the Portal can now do is notice when the evidence they leave behind is missing, stale, or inconsistent, and every finding names the command that fixes it rather than just reporting a problem.

  Host enrolment is checked by comparing the host's own enrolment against the Portal's machine registration: tenant or enrollment-id drift, a revoked registration, a host enrolled but never registered, a client certificate that is not the one the Portal expects, and certificate expiry with advance warning. Each side looks healthy examined alone, which is exactly why they are compared.

- `etl-sql admin restore` and `--validate` now record their outcome under job-state `admin-restore`, mirroring what `admin backup` already did, so the Portal can show when an archive was last proven readable and not only when one was last written. A backup nobody has ever restored is a hope rather than a recovery plan, so "never proven readable" is reported as a finding instead of a blank.
