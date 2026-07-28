# CREATE JOB / Job Scheduling

Registers a script or report job with the Orchestrator. Jobs, cron schedules, and notification
destinations are independent catalog objects; link them explicitly with `ALTER JOB`.

## Syntax

```text
CREATE [OR ALTER|OR REPLACE] JOB JobName
  FOR SCRIPT|REPORT 'target-path'
  [WITH (
    MAX_RETRIES = n,
    RETRY_DELAY = seconds,
    DISPLAY_NAME = 'label',
    DESCRIPTION = 'description'
  )];

ALTER JOB JobName ADD|REMOVE SCHEDULE ScheduleName;
ALTER JOB JobName ADD|REMOVE NOTIFICATION NotificationName
  ON SUCCESS|FAILURE|COMPLETION;
```

- **JobName** — Stable, unquoted, case-insensitive identity.
- **FOR SCRIPT** — Names a `.etlsql` file or `orch://` bundle entry. Unversioned bundle paths are
  pinned to their latest version when the job is written.
- **FOR REPORT** — Names the report path refreshed when the job completes.
- **MAX_RETRIES** — Retry attempts after a failed execution; default `0`.
- **RETRY_DELAY** — Initial retry delay in seconds; default `30`.
- **CREATE OR ALTER** — Updates the definition and preserves existing links.
- **CREATE OR REPLACE** — Fully redefines the job and removes its schedule and notification links.

## Examples

```sql
CREATE SCHEDULE EveryThirtyMinutes
  ON '*/30 * * * *'
  AT TIME ZONE 'UTC';

CREATE JOB CleanupJob
  FOR SCRIPT 'scripts/cleanup.etlsql';

ALTER JOB CleanupJob ADD SCHEDULE EveryThirtyMinutes;
```

```sql
CREATE SCHEDULE NightlyAtTwo
  ON '0 2 * * *'
  AT TIME ZONE 'America/New_York';

CREATE JOB NightlyArchive
  FOR SCRIPT 'orch://archive/main.etlsql'
  WITH (MAX_RETRIES = 3, RETRY_DELAY = 60);

ALTER JOB NightlyArchive ADD SCHEDULE NightlyAtTwo;
```

## Remote Orchestrator Scheduling

Use the same statements inside an `EXECUTE` block. There is no `AT <server>` suffix.

```sql
EXECUTE orch_conn BEGIN
  CREATE SCHEDULE EveryThirtyMinutes ON '*/30 * * * *' AT TIME ZONE 'UTC';
  CREATE JOB CleanupJob FOR SCRIPT 'scripts/cleanup.etlsql';
  ALTER JOB CleanupJob ADD SCHEDULE EveryThirtyMinutes;
END;
```

## Job Management

```sql
ALTER JOB CleanupJob SET TARGET = 'scripts/cleanup-v2.etlsql';
ALTER JOB CleanupJob SET (MAX_RETRIES = 5, DISPLAY_NAME = 'Cleanup — production');

SHOW JOBS;
SHOW JOB HISTORY CleanupJob;

DROP JOB IF EXISTS CleanupJob;
KILL JOB 1023;
```

## Notes

- Cron schedules are minute-granularity; `EVERY n SECONDS` has no replacement.
- `CREATE JOB ... ON SCHEDULE ... AS ...` is retired. Put executable statements in a script file.
- A new job does not run until at least one enabled schedule is attached or it is triggered manually.

References:

- [Orchestrator Jobs](README.md)
- [Job Scheduling](../../administration/orchestration/job-scheduling.md)
