---
trigger: $ftp
label: CREATE CONNECTION … ON FTP
description: FTP connection for remote file transfer
---
CREATE CONNECTION «ConnName» AS FTP(
  HOST     = '«ftp.example.com»',
  USER     = '«username»',
  PASSWORD = '«password»'
);
