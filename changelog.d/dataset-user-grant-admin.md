### Added

- The Admin dataset permissions panel now shows grants made directly to a user alongside group grants, each labelled with its principal type, and can revoke either. `GET /api/datasets/{id}/acl` carries a `principalKind` on every entry, and `DELETE /api/datasets/{id}/acl/user/{userId}` revokes a direct grant and invalidates that user's sessions. This completes the dataset half of "authorship is not permission": a creator's Owner grant was enforced and revocable in the database, but invisible in the product — a grant an administrator cannot see is a grant they cannot account for.
