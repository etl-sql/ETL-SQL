---
trigger: $portal_alert
label: EXECUTE portal BEGIN CREATE ALERT
description: Create a data-driven alert that emails when a condition is met
---
EXECUTE portal BEGIN
  CREATE ALERT '«AlertName»'
    FOR REPORT '«Report Name»'
    WHEN '«column < threshold»'
    NOTIFY '«recipient@corp.com»'
    WITH (SCHEDULE = '«0 8 * * 1-5»');
END;
