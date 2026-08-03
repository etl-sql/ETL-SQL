### Added

- Added dataset at-rest key posture at `GET /api/admin/datasets/at-rest-key/posture`: the per-version inventory of encrypted caches, a rotation preflight, verification that rotation finished, and rollback guidance. Rotation itself is unchanged.

  Preflight is the reason it exists. A cache encrypted under a key version that is no longer configured can be neither rotated **nor read**, and the only way to discover that was to start the rotation and read the failure list afterwards. Those datasets are now named beforehand, with the reason and the remedy. Key *versions* are non-secret identifiers and are named; key material never appears — a key is reported as configured or not, and a test asserts the configured key value is absent from the entire response.

- Added secret and connection posture at `GET /api/admin/credentials/posture`, which resolves the two against each other: which connections reference which secrets, which references do not resolve, when each secret was last rotated, which secrets nothing references, and which secrets a configuration export would require the promotion target to supply.

  The failure this exists for is invisible on either page alone. A connection referencing a secret that was renamed, disabled, or never created appears healthy in the connections list and healthy in the secrets list; the break lives only in the join between them and surfaces the first time something runs. No secret value is read to build the view — references are matched by name, because resolving them would mean decrypting every secret to render a page.
