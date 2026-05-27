# DOCKER Operations
Start, stop, pause, resume, and close Docker containers. Use these to spin up sidecar services for a script run and tear them down when done.

Syntax:
  START_DOCKER  '<image>'  [AS <alias>];
  STOP_DOCKER   <alias>;
  PAUSE_DOCKER  <alias>;
  RESUME_DOCKER <alias>;
  CLOSE_DOCKER  <alias | image>;

START_DOCKER pulls the image if not present locally and starts a container. AS <alias> assigns a name for subsequent operations.

```sql
-- Start a local Postgres instance for testing
START_DOCKER 'postgres:15' AS TestPG;

-- Wait for the database to accept connections
WAITFOR DELAY '00:00:05';

CREATE CONNECTION TestDB AS POSTGRES(
  HOST     = 'localhost',
  PORT     = 5432,
  DATABASE = 'postgres',
  USER     = 'postgres',
  PASSWORD = 'postgres'
);

SELECT * INTO #data FROM TestDB.public.my_table;

-- Tear down when done
CLOSE_DOCKER TestPG;
```

References:
- [Specialized Operations](../../../../../Docs/Reference/Specialized_Operations.md)
