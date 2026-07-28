---
trigger: $job
label: CREATE JOB & link schedule
description: Script job with a reusable cron schedule
---
CREATE SCHEDULE «ScheduleName»
  ON '«0 2 * * *»'
  AT TIME ZONE '«UTC»';

CREATE JOB «JobName»
  FOR SCRIPT '«scripts/job.etlsql»';

ALTER JOB «JobName» ADD SCHEDULE «ScheduleName»;
