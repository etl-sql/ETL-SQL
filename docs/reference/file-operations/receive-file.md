Downloads a file from a remote server via an FTP or SFTP connection.

VERBOSE:
  RECEIVE FILE FROM 'remotePath' TO 'localPath' AT connectionName

SHORTHAND:

Parameters:
  remotePath   - Path to the file on the remote server
  localPath    - Local destination path for the downloaded file
  connectionName - Name of an FTP or SFTP connection

Examples:
  RECEIVE FILE FROM '/data/report.csv' TO 'C:\downloads\report.csv' AT MyFtp;

References:
- [Specialized Operations](../../administration/platform/README.md)
