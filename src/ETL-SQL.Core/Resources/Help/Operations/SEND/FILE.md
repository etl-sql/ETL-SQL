Transfers a local file to a remote server via an FTP or SFTP connection.

VERBOSE:
  SEND FILE 'localPath' TO 'remotePath' AT connectionName [WITH (OVERWRITE = TRUE|FALSE)]

SHORTHAND:
  SEND FILE('localPath', connectionName, 'remotePath'[, overwrite])

Parameters:
  localPath    - Path to the local file to upload
  remotePath   - Destination path on the remote server
  connectionName - Name of an FTP or SFTP connection
  OVERWRITE    - If TRUE, overwrites an existing remote file (default: FALSE)

Examples:
  SEND FILE 'C:\exports\report.csv' TO '/data/report.csv' AT MyFtp WITH (OVERWRITE = TRUE);
  SEND FILE('C:\exports\report.csv', MyFtp, '/data/report.csv', TRUE);

References:
- [Specialized Operations](../../../../../Docs/Reference/Specialized_Operations.md)
