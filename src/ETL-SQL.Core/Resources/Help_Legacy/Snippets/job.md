---
trigger: $job
label: CREATE JOB … ON SCHEDULE EVERY … AS
description: Scheduled job that runs a script or statement on a recurring interval
---
CREATE JOB «JobName»
  ON SCHEDULE EVERY «1» «HOUR»
AS
  RUN SCRIPT '«scripts/job.etlsql»';
