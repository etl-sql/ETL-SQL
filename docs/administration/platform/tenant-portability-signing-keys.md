# Tenant Portability Signing Keys

Tenant portability manifests are signed by the exporting operator with an OpenPGP signing key.
This runbook defines how operators publish, rotate, retire, revoke, and retain those verification
keys so a customer can validate an export after access to the source deployment ends.

The signing key proves export provenance. It is separate from the tenant recipient key that encrypts
payloads, and the two roles must never share a key pair.

## Published artifacts

Each operator publishes these artifacts from its authenticated customer domain:

- `/.well-known/etl-sql/tenant-export-signing-keys.asc` — an ASCII-armored OpenPGP public keyring
  containing every current and retired verification key that may validate a retained bundle.
- `/.well-known/etl-sql/tenant-export-signing-keys.json` — a UTF-8 index containing the operator
  identity, key fingerprints, SHA-256 digest of each immutable public-key artifact, lifecycle state
  (`prepublished`, `active`, `retired`, or `revoked`), activation/retirement timestamps, and any
  compromise-effective timestamp.
- `/.well-known/etl-sql/tenant-export-signing-keys/<fingerprint>.asc` — an immutable, versioned copy
  of each public key. It remains available after retirement.

The index and keyring are served over HTTPS with no redirect to another registrable domain. Release
notes and the tenant administration surface publish the current key fingerprint through a second
channel. Customers compare that fingerprint before trusting a first download; HTTPS alone is not a
substitute for trust-on-first-use verification.

Operators retain public keys and lifecycle metadata for at least the longest supported bundle
retention period plus the product compatibility window. A retired public key is not deleted merely
because its private key has been destroyed.

## Routine rotation

1. Generate a signing-only OpenPGP key pair in the approved operator key boundary. Store the private
   key with least-privilege filesystem or secret-manager access; store its passphrase separately.
2. Publish the public key and index entry as `prepublished` at least 30 days before first use. Publish
   its fingerprint through the independent release/tenant-administration channel.
3. Verify both published artifacts from outside the operator network and record their SHA-256
   digests in the change record.
4. At activation, change the index state to `active`, deploy the private key to the export worker,
   and create a canary bundle. Verify it offline with the downloaded public keyring using
   `etl-sql admin tenant validate --require-signature --operator-key <keyring>`.
5. Keep the prior key available for signing rollback for no more than seven days, but never select it
   for a new export after the new key is accepted. Then destroy or cryptographically erase the prior
   private key and mark its public entry `retired`.
6. Retain the retired public key, fingerprints, lifecycle dates, canary evidence, and rotation audit
   record. Existing bundles continue to validate against the published keyring.

Only one key is `active` for new signatures. Prepublication overlap is verification overlap, not a
period in which workers may choose either signer nondeterministically.

## Emergency revocation

If a signing private key may be exposed:

1. Stop new exports and remove the key from every export worker.
2. Mark the key `revoked`, publish its OpenPGP revocation certificate, and record the earliest known
   compromise time in the index and incident record.
3. Generate and publish a replacement immediately; the normal 30-day prepublication interval is
   waived, but independent fingerprint publication and an offline canary verification are not.
4. Notify affected tenants and re-export affected bundles with the replacement key.

An OpenPGP signature alone is not a trusted timestamp. A bundle signed by the compromised key is
not treated as authentic merely because its manifest claims a creation time before the incident.
It may be accepted only when an independent immutable export-audit record proves the signature was
created before the compromise-effective time; otherwise it must be replaced.

## Customer custody and verification

At export time the customer retains the bundle, the exact public key or keyring used to verify it,
the independently checked fingerprint, and the key-index snapshot. Validation is offline:

```text
etl-sql admin tenant validate \
  --bundle <bundle-directory> \
  --operator-key tenant-export-signing-keys.asc \
  --require-signature
```

Customers must not replace their retained keyring solely because a bundle contains or links to a
different key. The bundle is untrusted until verification succeeds. On rotation, add a key only
after checking its fingerprint through the independent operator channel.

## Audit evidence

Every activation, retirement, revocation, and emergency replacement records the actor, approved
change or incident, full fingerprint, public-artifact SHA-256 digest, lifecycle timestamps, affected
export-worker deployment, external publication check, and canary bundle validation result. The
record contains no private-key material or passphrase.

## References

- [Tenant Portability Architecture](../../architecture/tenant-portability.md)
- [Service Accounts](../../reference/portal-commands/service-accounts.md)
- [CLI Reference: admin tenant](../../reference/cli/admin-tenant.md)
