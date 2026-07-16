---
trigger: $portal_show
label: EXECUTE portal BEGIN SHOW portal info
description: Retrieve portal users, reports, sessions, or permissions into a temp table
---
EXECUTE portal BEGIN
  SHOW USERS INTO #users;
END;

SELECT * FROM #users;
