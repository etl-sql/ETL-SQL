---
trigger: $portal_refresh
label: EXECUTE portal BEGIN CREATE REFRESH JOB
description: Schedule automated dataset refresh for a report via Orchestrator
---
EXECUTE portal BEGIN
  CREATE REFRESH JOB FOR REPORT '«Report Name»'
    SCHEDULE '«0 2 * * *»'
    AT «orch_conn»;
END;
