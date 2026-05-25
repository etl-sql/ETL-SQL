---
trigger: $sftp
label: CREATE CONNECTION … ON SFTP
description: SFTP connection for remote file transfer via SSH key or password
---
CREATE CONNECTION «ConnName» ON SFTP(
  HOST     = '«sftp.example.com»',
  USER     = '«username»',
  KEYFILE  = '«path/to/id_rsa»'
);
