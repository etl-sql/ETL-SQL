# FILE-OPERATIONS Reference

[« Back to parent](../README.md)

| Page | Description |
| :--- | :--- |
| [Advanced File Operations](advanced-file-operations.md) | Advanced filesystem statements that replace custom script or program implementations in data |
| [bulk-insert](bulk-insert.md) | BULK INSERT loads a flat file directly into a connection table in high-throughput batches, bypassing the #temp table staging step. |
| [COMPRESS / DECOMPRESS](compress-file.md) | Compresses or decompresses files and directories using ZIP or GZIP format. |
| [CONVERT FILE ENCODING](convert-file-encoding.md) | Performs stream-based transcoding from one encoding standard to another. |
| [COPY](copy-file.md) | Copies a file or directory from one location to another, including across connections (local, SFTP, S3). |
| [PGP_KEY_PAIR](create-pgp-key-pair.md) | Generates an OpenPGP key pair (RSA) and writes the private and public key files to the specified path. |
| [SSH_KEY_PAIR](create-ssh-key-pair.md) | Generates an RSA or ECDSA SSH key pair and writes the private and public key files to the specified path. |
| [DIRECTORY Operations](directory.md) | File-system directory management commands. These operate on the local or UNC file system without requiring a CREATE CONNECTION. |
| [DOCKER Operations](docker.md) | Start, stop, pause, resume, and close Docker containers. Use these to spin up sidecar services for a script run and tear them down when done. |
| [ENCRYPT / DECRYPT](encrypt-file.md) | Encrypts or decrypts files on disk. Also covers `ENC:` credential values and session password management. |
| [FILE Operations](file.md) | File-level management commands for copying, moving, renaming, deleting, compressing, encrypting, and decrypting individual files. |
| [MERGE FILES](merge-files.md) | Concatenates multiple files (supports wildcards or array inputs) into a single destination file. |
| [receive-file](receive-file.md) | Downloads a file from a remote server via an FTP or SFTP connection. |
| [send-email](send-email.md) | Sends an email via an SMTP connection. |
| [send-file](send-file.md) | Transfers a local file to a remote server via an FTP or SFTP connection. |
| [SPLIT FILE](split-file.md) | Splits a larger text file into multiple chunk files based on row count or byte size. |
| [SYNC DIRECTORY](sync-directory.md) | Mirrors a source directory to a destination directory, doing fast file transfers based on modified |
| [TRANSFER Operations](transfer.md) | SEND FILE and RECEIVE FILE move files between the local file system and a remote server connection (SFTP, FTP, or Azure Blob). |
| [VERIFY FILE INTEGRITY](verify-file-integrity.md) | Computes file hashes and validates them against expected hex strings or a companion checksum file. |
| [WAITFOR FILE UNLOCKED](waitfor-file-unlocked.md) | Blocks pipeline execution until a file arrives on the filesystem and is fully unlocked (not being |
