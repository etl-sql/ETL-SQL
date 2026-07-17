# FILE-OPERATIONS Reference

[« Back to parent](../README.md)

| Page | Description |
| :--- | :--- |
| [bulk-insert](bulk-insert.md) | BULK INSERT loads a flat file directly into a connection table in high-throughput batches, bypassing the #temp table staging step. |
| [COMPRESS / DECOMPRESS](compress-file.md) | Compresses or decompresses files and directories using ZIP or GZIP format. |
| [COPY](copy-file.md) | Copies a file or directory from one location to another, including across connections (local, SFTP, S3). |
| [PGP_KEY_PAIR](create-pgp-key-pair.md) | Generates an OpenPGP key pair (RSA) and writes the private and public key files to the specified path. |
| [SSH_KEY_PAIR](create-ssh-key-pair.md) | Generates an RSA or ECDSA SSH key pair and writes the private and public key files to the specified path. |
| [DIRECTORY Operations](directory.md) | File-system directory management commands. These operate on the local or UNC file system without requiring a CREATE CONNECTION. |
| [DOCKER Operations](docker.md) | Start, stop, pause, resume, and close Docker containers. Use these to spin up sidecar services for a script run and tear them down when done. |
| [ENCRYPT / DECRYPT](encrypt-file.md) | Encrypts or decrypts files on disk. Also covers `ENC:` credential values and session password management. |
| [FILE Operations](file.md) | File-level management commands for copying, moving, renaming, deleting, compressing, encrypting, and decrypting individual files. |
| [receive-file](receive-file.md) | Downloads a file from a remote server via an FTP or SFTP connection. |
| [send-email](send-email.md) | Sends an email via an SMTP connection. |
| [send-file](send-file.md) | Transfers a local file to a remote server via an FTP or SFTP connection. |
| [ETL-SQL Specialized Operations & Automation](specialized-operations.md) | This document is the technical reference for ETL-SQL's non-query automation features: filesystem management, remote file transfer, email notificati... |
| [TRANSFER Operations](transfer.md) | SEND FILE and RECEIVE FILE move files between the local file system and a remote server connection (SFTP, FTP, or Azure Blob). |
