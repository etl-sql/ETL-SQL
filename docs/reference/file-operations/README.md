# FILE-OPERATIONS Reference

[« Back to parent](../README.md)

| Page | Description |
| :--- | :--- |
| [Advanced File Operations](advanced-file-operations.md) | Advanced filesystem statements that replace custom script or program implementations in data |
| [BULK INSERT](bulk-insert.md) | BULK INSERT streams a flat file into a table in bounded batches. It validates values at the target |
| [COMPRESS / DECOMPRESS FILE](compress-file.md) | Compresses or extracts individual files and directory hierarchies on disk using `GZIP` or `ZIP` archive formats. |
| [CONVERT FILE ENCODING](convert-file-encoding.md) | Performs stream-based transcoding from one encoding standard to another. |
| [COPY](copy-file.md) | Copies a local file or directory from one sandboxed location to another. |
| [PGP_KEY_PAIR](create-pgp-key-pair.md) | Generates an OpenPGP key pair (RSA) and writes the private and public key files to the specified path. |
| [SSH_KEY_PAIR](create-ssh-key-pair.md) | Generates an RSA or ECDSA SSH key pair and writes the private and public key files to the specified path. |
| [DIRECTORY Operations](directory.md) | File-system directory management commands. These operate on the local or UNC file system without requiring a CREATE CONNECTION. |
| [DOCKER Operations](docker.md) | Start, stop, pause, resume, and close Docker containers. Use these to spin up sidecar services for a script run and tear them down when done. |
| [ENCRYPT / DECRYPT FILE](encrypt-file.md) | Encrypts or decrypts local files and staged datasets on disk using AES-256 or PGP. Also governs session-level credential encryption (`ENC:` strings... |
| [FILE Operations](file.md) | File-level management commands for copying, moving, renaming, deleting, compressing, encrypting, and decrypting individual files. |
| [MERGE FILES](merge-files.md) | Concatenates multiple files (supports wildcards or array inputs) into a single destination file. |
| [MOVE FILE](move-file.md) | Moves a file from one location to another. Use `TO DIRECTORY` when the destination should keep the source file name and optionally add a date suffix. |
| [RECEIVE FILE](receive-file.md) | Downloads a remote file from an `SFTP`, `FTP`, or `AZURE_BLOB` connection into local engine staging storage for processing and ingestion. |
| [SEND EMAIL](send-email.md) | Sends plain text or formatted HTML emails with optional file attachments via a configured `SMTP` connection. Ideal for automated pipeline reports, ... |
| [SEND FILE](send-file.md) | Transfers a local file or exported dataset to a remote server over a secure `SFTP`, `FTP`, or `AZURE_BLOB` connection. |
| [SPLIT FILE](split-file.md) | Splits a larger text file into multiple chunk files based on row count or byte size. |
| [SYNC DIRECTORY](sync-directory.md) | Mirrors a source directory to a destination directory, doing fast file transfers based on modified |
| [TRANSFER Operations](transfer.md) | SEND FILE and RECEIVE FILE move files between the local file system and a remote server connection (SFTP, FTP, or Azure Blob). |
| [VERIFY FILE INTEGRITY](verify-file-integrity.md) | Computes file hashes and validates them against expected hex strings or a companion checksum file. |
| [WAITFOR FILE UNLOCKED](waitfor-file-unlocked.md) | Blocks pipeline execution until a file arrives on the filesystem and is fully unlocked (not being |
