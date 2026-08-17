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
- `USE PASSWORD = 'literal'` is a shorthand for local/testing convenience. On save it is rewritten to `USE PASSWORD PROMPT` unless `SET ALLOW_PLAINTEXT_SECRETS = ON` is present.
- `USE PASSWORD PROMPT` is the secure interactive form; the password is not written to disk.
- If `SET CONNECTION_ENCRYPTION = ON`, save helpers may use a literal `USE PASSWORD` value to encrypt connection details before rewriting it to `USE PASSWORD PROMPT`.
- In VS Code's normal text editor, source text cannot be converted into a true password field while typing. Prefer `USE PASSWORD PROMPT` and save-time policies for source-controlled scripts.
- Published Orchestrator bundles remove `USE PASSWORD` statements from the stored copy after secrets are re-encrypted for the lockbox.
- `SET SHOW_SECRETS = ON` controls display masking only; it does not permit plaintext master passwords to remain in saved source.
- `USE SETS !name` replaces any previously active set of the same name.
- Sets can be created inline with `CREATE SETS` or loaded from external `.sets` files.
- See: [CREATE SETS](create-sets.md), ENCRYPT, DECLARE

References:
- [Variables and Parameters](README.md)
