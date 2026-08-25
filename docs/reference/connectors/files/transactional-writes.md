# TRANSACTIONAL FILE WRITES

`TRANSACTIONAL=ON` prevents consumers from observing a partial local file and preserves the prior
target when serialization, encryption, compression, cancellation, or publication fails.

## Syntax

```sql
CREATE CONNECTION export_file AS JSON(
  PATH = 'C:\Exports\orders.json',
  TRANSACTIONAL = ON
);
```

## Contract

- **Supported local connectors** — `FLATFILE`/`CSV`, `JSON`, `XML`, `EXCEL`, and `PARQUET`.
- **Collision safety** — each writer receives a unique engine-owned sibling stage named from the exact target.
- **Path boundary** — the target is resolved and authorized before I/O; the sibling stage is resolved through the same execution context and must remain in the target directory.
- **Commit point** — all serialization, compression, and encryption completes before one same-directory replacement rename publishes the file.
- **Failure and cancellation** — the stage is removed and an existing target is left unchanged.
- **Concurrent writers** — stages do not collide; each complete rename is atomic and the last successful publisher wins.
- **Process loss** — stages older than 24 hours are reconciled on the next transactional write for that exact target. Fresh and unrelated stages are not removed.
- **Append and retry** — append first copies the prior target into the private stage. A retry creates a new stage and never resumes a partial stage.
- **Multi-output formats** — the supported connectors above publish one final artifact. No group-level atomicity is claimed across multiple target files.

Remote SFTP uses a separate provider contract: `ATOMIC_UPLOAD=ON` uploads a unique sibling stage and
requires the server's POSIX rename extension. Replacement never deletes the old target first. If the
extension or rename permission is unavailable, publication fails and the old target remains. FTP does
not advertise transactional publication because its protocol cannot guarantee atomic replacement.

## Example

```sql
CREATE CONNECTION safe_csv AS FLATFILE(
  PATH = 'C:\Exports\daily.csv',
  HEADER = ON,
  TRANSACTIONAL = ON
);

INSERT INTO safe_csv.FILE
SELECT * FROM #validated_rows;
```

## Troubleshooting

- **Staging Directory Permission**: Ensure directory permits write and rename operations.
- **Lock Contention**: Clean up orphaned staging files if processes terminate abnormally.

## References

- [File Connectors](README.md)
- [SFTP](../services/sftp.md)
- [Connector Standards](../../../architecture/standards/connectors-standards.md)


## Authentication

Transactional write operations use local or remote file system permissions.