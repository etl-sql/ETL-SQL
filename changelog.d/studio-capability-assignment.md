### Added

- Studio capabilities can now be granted to a group and to a service account, not only mapped to a role in configuration. Previously changing who may publish, commit, or push meant editing `Portal:Studio:RoleCapabilities` and restarting, and could not be expressed for anything narrower than an entire role.

  `GET` and `PUT /api/admin/groups/{id}/studio-capabilities` manage a group's set and reject an unknown capability name rather than storing a typo that would read as a successful grant and do nothing. Grants are resolved at sign-in and at refresh and carried as `studio_capability` claims, so the per-request check stays a claim lookup; changing a group's capabilities signs its members out, exactly as changing an ACL does, rather than leaving a live session holding authority that was just withdrawn.

  Service accounts carry their own capability set, capped by their owner's at token issue in the same way their roles already were — an account that could exceed its owner would be a way to retain authority the owner had lost.
