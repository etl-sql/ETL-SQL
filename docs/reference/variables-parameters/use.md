# USE
Applies session-level settings: encryption passwords or named sets.

## USE PASSWORD
Sets the active encryption/decryption password for the session. Required before accessing `SENSITIVE` or `ENCRYPTED` variables and `ENC:` connection credentials.
```sql
USE PASSWORD = 'my-secret-passphrase';
USE PASSWORD PROMPT;
```

## USE SETS
Activates a named environment set (defined with CREATE SETS or loaded from a .sets file). Variables in the set become available as `!SetName` references.
```sql
USE SETS !Production;
```

## Notes
- `USE PASSWORD` must appear before any statement that reads an `ENC:` value or a SENSITIVE/ENCRYPTED variable.
- `USE PASSWORD = 'literal'` is for initial authoring convenience or migration only, never for durable storage. When `ALLOW_PLAINTEXT_SECRETS` is `OFF` (the default), saving in VS Code, the ETL-SQL TUI Editor, or ETL-SQL Studio automatically rewrites `USE PASSWORD = '...'` to `USE PASSWORD PROMPT;` and encrypts plaintext connection secrets (`ENC:...`) using that password, clearing editor undo history so `Ctrl+Z` does not restore plaintext.
- Running an unsealed file-backed script directly via CLI or engine when `ALLOW_PLAINTEXT_SECRETS` is `OFF` also seals and rewrites the script on disk before execution.
- `USE PASSWORD PROMPT;` is the interactive sealed form; the password is prompted at runtime and never stored on disk.
- For headless or production jobs, provide the key with `--pass` / `ETL_SQL_MASTER_PASSWORD`, or read credentials from a secret store (`SECRET:...` / vault) instead of hardcoding `USE PASSWORD` literals.
- `SET ALLOW_PLAINTEXT_SECRETS = ON` is an unsafe local-development escape hatch that keeps plaintext on disk (with warnings and subject to governance policy) and does not silence security or lint checks.
- Published Orchestrator bundles remove `USE PASSWORD` statements from the stored copy after secrets are re-encrypted for the lockbox.
- `SET SHOW_SECRETS = ON` controls display masking only; it does not permit plaintext master passwords to remain in saved source.
- `USE SETS !name` replaces any previously active set of the same name.
- Sets can be created inline with `CREATE SETS` or loaded from external `.sets` files.
- See: [CREATE SETS](create-sets.md), ENCRYPT, DECLARE

References:
- [Variables and Parameters](README.md)
