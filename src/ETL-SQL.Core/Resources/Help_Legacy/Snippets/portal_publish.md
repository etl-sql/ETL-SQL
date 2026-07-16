---
trigger: $portal_publish
label: EXECUTE portal BEGIN PUBLISH REPORT
description: Publish a report script to a portal folder
---
EXECUTE portal BEGIN
  PUBLISH REPORT '«C:\Reports\report.rptsql»'
    TO FOLDER '«FolderName»'
    WITH (NAME = '«Report Display Name»', REPLACE = ON);
END;
