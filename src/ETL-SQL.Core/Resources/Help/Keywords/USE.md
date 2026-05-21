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

-- After activation, reference members in queries
SELECT * FROM #orders WHERE Region IN !Regions;
```

## Notes
- `USE PASSWORD` must appear before any statement that reads an `ENC:` value or a SENSITIVE/ENCRYPTED variable.
- `USE PASSWORD = 'literal'` stores the password in the script source. Use it only for local/testing convenience.
- `USE PASSWORD PROMPT` is the secure interactive form; the password is not written to disk.
- Published Orchestrator bundles remove `USE PASSWORD` statements from the stored copy after secrets are re-encrypted for the lockbox.
- The password is held in session memory only and is never written to disk or logs.
- `USE SETS !name` replaces any previously active set of the same name.
- Sets can be created inline with `CREATE SETS` or loaded from external `.sets` files.
- See: CREATE SETS, ENCRYPT, DECLARE
