---
trigger: $portal_alert
label: EXECUTE portal BEGIN CREATE ALERT
description: Create a portal visual threshold alert and attach a notification
---
EXECUTE portal BEGIN
  CREATE ALERT «AlertName»
    FOR REPORT '«/Folder/Report Name»'
    WHEN VISUAL «VisualName» «>=» «1000»
    WITH (DESCRIPTION = '«Alert description»');
  ALTER ALERT «AlertName» ADD NOTIFICATION «orchestrator_alias».«NotificationName»;
END;
