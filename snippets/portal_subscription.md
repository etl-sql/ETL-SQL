---
trigger: $portal_sub
label: EXECUTE portal BEGIN CREATE SUBSCRIPTION
description: Schedule an email delivery of a report on a cron schedule
---
EXECUTE portal BEGIN
  CREATE SUBSCRIPTION '«SubName»'
    FOR REPORT '«Report Name»'
    TO '«recipient@corp.com»'
    WITH (SCHEDULE = '«0 7 * * 1»', FORMAT = PDF);
END;
