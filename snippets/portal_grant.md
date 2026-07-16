---
trigger: $portal_grant
label: EXECUTE portal BEGIN GRANT folder access
description: Grant a user or group access to a portal folder
---
EXECUTE portal BEGIN
  GRANT '«FolderName»' TO USER '«username»' WITH (ACCESS = READ);
END;
