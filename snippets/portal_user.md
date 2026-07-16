---
trigger: $portal_user
label: EXECUTE portal BEGIN CREATE USER
description: Create a portal user with role and email
---
EXECUTE portal BEGIN
  CREATE USER '«username»' WITH (
    PASSWORD = '«password»',
    EMAIL    = '«user@corp.com»',
    ROLE     = «VIEWER»
  );
END;
