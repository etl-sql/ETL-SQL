Downloads a file from a remote server via an FTP or SFTP connection.

VERBOSE:
  RECEIVE FILE FROM 'remotePath' TO 'localPath' AT connectionName

SHORTHAND:
  RECEIVE FILE(connectionName, 'remotePath', 'localPath')

Parameters:
  remotePath   - Path to the file on the remote server
  localPath    - Local destination path for the downloaded file
  connectionName - Name of an FTP or SFTP connection

Examples:
  RECEIVE FILE FROM '/data/report.csv' TO 'C:\downloads\report.csv' AT MyFtp;
  RECEIVE FILE(MyFtp, '/data/report.csv', 'C:\downloads\report.csv');
