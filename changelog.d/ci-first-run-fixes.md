### Fixed

- **The MSI upgrade gate could never have passed.** `Get-MsiProperty` in `Test-MsiUpgrade.ps1`
  returned `Object[]` — `('', '{GUID}', '')` — because two COM calls emitted to the pipeline
  unsuppressed. PowerShell's `-ne` against an array is a *filter*, not a comparison, so the
  UpgradeCode check reported "UpgradeCode changed" for two identical codes and failed the run.

  Reproduced against the shipped v0.16.0 and v0.17.0 MSIs rather than inferred from the log: the
  reader now returns a single trimmed `String`, and identical codes compare equal. This was the
  gate's first ever execution, which is exactly what it was built to discover — though it found a
  defect in itself rather than in the installer.

- **`feedback.js` was missing from the `.gitattributes` LF pin list**, so a Windows CI checkout
  converted it to CRLF, the canonical and host copies stopped being byte-identical, and
  `sync-assets.js -Check` failed the build for a file whose content was correct. It was the only
  shared asset not pinned — added when the feedback dialogs were unified, without the matching
  attribute line. The file's own comment predicted this failure mode in advance.

### Added

- `SharedAssetLineEndingPinTests` asserts every file under
  `ETL-SQL.ReportRuntime/Resources/Shared` is pinned to LF, asking `git check-attr` so the answer
  reflects real attribute resolution. `.gitattributes` already described the rule in a comment; the
  comment did not stop the omission, and the cost of finding it was a full CI run.

### Known

- `npm audit` fails the VS Code extension job on two high-severity advisories in transitive
  dependencies (`brace-expansion`, `undici`). Pre-existing dependency drift rather than a code
  change; Dependabot has branches open. Recorded here so the red build is not mistaken for a
  regression from this release's work.
