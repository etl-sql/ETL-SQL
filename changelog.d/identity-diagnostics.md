### Added

- Added identity-provider diagnostics at `GET /api/admin/identity/diagnostics`: OIDC reachability and startup validation findings, LDAP configuration, the claim value each provider-managed group expects, how many federated users have landed in no mapped group, and **break-glass readiness** — whether any active local administrator could sign in with the identity provider unreachable. An estate that federates every account, administrators included, is one provider outage away from nobody being able to correct the provider's configuration, and that is worth knowing before it happens rather than during.

  Configured secrets are reported as presence flags. A test asserts the configured client secret appears nowhere in the response at all, not merely that the obvious field omits it.

- Added `POST /api/admin/identity/diagnostics/group-mapping-test`, which resolves claim values against the configured group mappings without anyone signing in and names the values that match nothing. A claim that maps to no group is sign-in working while authorization quietly does not — the kind of gap normally found by a user reporting they cannot see something.
