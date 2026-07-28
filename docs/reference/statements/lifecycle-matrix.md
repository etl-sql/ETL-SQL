# Lifecycle Capability Matrix

ETL-SQL uses one object-lifecycle vocabulary where an object has stable identity:
`CREATE`, `CREATE OR ALTER`, `CREATE OR REPLACE`, `ALTER`, `DROP`, and `DROP IF EXISTS`.
Unsupported object/mode pairs are parser errors; they must not parse as a plain `CREATE`.

Legend: ✓ supported, — unsupported, n/a not an object-lifecycle form.

## Core and Orchestrator objects

| Object kind | `CREATE` | `CREATE IF NOT EXISTS` | `CREATE OR ALTER` | `CREATE OR REPLACE` | `ALTER` | `DROP` | `DROP IF EXISTS` | Notes |
| :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :--- |
| `CONNECTION` | ✓ | — | ✓ | ✓ | ✓ | ✓ | ✓ | Session or shared-catalog connection, depending on host/target. |
| `TABLE` | ✓ | ✓ | — | ✓ | ✓ | ✓ | ✓ | `CREATE OR REPLACE TABLE` drops and recreates the table definition/data. |
| `INDEX` / `UNIQUE INDEX` | ✓ | — | — | — | — | ✓ | ✓ | Indexes are rebuilt by dropping and recreating them. |
| `PROCEDURE` | ✓ | — | ✓ | ✓ | ✓ | ✓ | ✓ | `ALTER PROCEDURE` uses the same body form as `CREATE`. |
| `FUNCTION` | ✓ | — | ✓ | ✓ | ✓ | ✓ | ✓ | `ALTER FUNCTION` uses the same body form as `CREATE`. |
| `VIEW` | ✓ | — | ✓ | ✓ | ✓ | ✓ | ✓ | `CREATE OR ALTER VIEW` is the idempotent patch form. |
| `JOB` | ✓ | — | ✓ | ✓ | ✓ | ✓ | ✓ | `OR ALTER` preserves links; `OR REPLACE` drops schedule/notification links. |
| `SCHEDULE` | ✓ | — | ✓ | ✓ | ✓ | ✓ | ✓ | `DROP` is restricted while linked to a job. |
| `NOTIFICATION` | ✓ | — | ✓ | ✓ | ✓ | ✓ | ✓ | `DROP` is restricted while linked to a job. |
| `DIRECTORY` | ✓ | — | — | — | — | n/a | n/a | File operation, not a catalog object; use overwrite options where supported. |
| `SSH_KEY_PAIR` | ✓ | — | — | — | — | n/a | n/a | Key generation is a file operation. |
| `PGP_KEY_PAIR` | ✓ | — | — | — | — | n/a | n/a | Key generation is a file operation. |
| `SETS` | ✓ | — | — | — | — | ✓ | ✓ | Named set definitions are replaced by explicit drop/create. |
| `TAG` | ✓ | — | — | — | — | — | — | Scheduled for replacement by `INSERT`/`UPDATE`/`DELETE TAG`. |
| `LINEAGE` | ✓ | — | — | — | — | — | — | Scheduled for replacement by `INSERT`/`DELETE LINEAGE`. |

## Report-SQL objects

| Object kind | `CREATE` | `CREATE IF NOT EXISTS` | `CREATE OR ALTER` | `CREATE OR REPLACE` | `ALTER` | `DROP` | `DROP IF EXISTS` | Notes |
| :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :--- |
| `VISUAL` | ✓ | — | ✓ | ✓ | ✓ | ✓ | ✓ | `ALTER` patches supported clauses; visual type changes require `OR REPLACE`. |
| `PAGE` | ✓ | — | ✓ | ✓ | ✓ | ✓ | ✓ | `STRUCTURE`/`MAP` are layout redefinitions; use `OR REPLACE`. |
| `CONTAINER` | ✓ | — | ✓ | ✓ | ✓ | ✓ | ✓ | `STRUCTURE`/`MAP` are layout redefinitions; use `OR REPLACE`. |
| `BUTTON` | ✓ | — | ✓ | ✓ | ✓ | ✓ | ✓ | Page-style `CREATE BUTTON name AS (...)` form. |
| `STYLE` | ✓ | — | ✓ | ✓ | — | ✓ | ✓ | No standalone `ALTER STYLE`; redefine with `CREATE OR REPLACE STYLE`. |
| `NAVIGATION` | ✓ | — | ✓ | ✓ | — | ✓ | ✓ | No standalone `ALTER NAVIGATION`; redefine with `CREATE OR REPLACE NAVIGATION`. |
| `DATASET` | ✓ | — | ✓ | ✓ | — | ✓ | ✓ | Local/report dataset lifecycle uses `&name`; no report-scoped `ALTER DATASET`. |
| `TEMPLATE` | ✓ | — | ✓ | ✓ | ✓ | ✓ | ✓ | `ALTER TEMPLATE` patches `OPTIONS (...)`. |
| `THEME` | ✓ | — | ✓ | ✓ | — | ✓ | ✓ | No standalone `ALTER THEME`; redefine with `CREATE OR REPLACE THEME`. |

## Portal operational objects

These statements run inside `EXECUTE <portal_conn> BEGIN … END`. They are Portal operations, not
engine-owned replayable catalog objects, unless noted.

| Object kind | `CREATE` | `CREATE IF NOT EXISTS` | `CREATE OR ALTER` | `CREATE OR REPLACE` | `ALTER` | `DROP` | `DROP IF EXISTS` | Notes |
| :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :--- |
| `USER` | ✓ | — | — | — | ✓ | ✓ | — | `DROP USER` may use `CASCADE`, not `IF EXISTS`. |
| `GROUP` | ✓ | — | — | — | — | ✓ | — | `DROP GROUP` may use `CASCADE`, not `IF EXISTS`. |
| `FOLDER` | ✓ | — | — | — | ✓ | ✓ | — | Folder identity is a Portal path. |
| `REPORT` | n/a | n/a | n/a | n/a | ✓ | ✓ | — | Reports are published, not created with `CREATE REPORT`. |
| Portal `DATASET` | n/a | n/a | n/a | n/a | ✓ | ✓ | — | Portal dataset commands use quoted catalog identity. |
| `SHARE LINK` | ✓ | — | — | — | — | — | — | Generated resource; use expiry/revocation APIs where available. |
| `EMBED TOKEN` | ✓ | — | — | — | — | — | — | Generated resource; not redefined by name. |
| `SAVED VIEW` | ✓ | — | — | — | — | ✓ | — | Identity is `(report, saved-view name)`. |
| `ALERT` | ✓ | — | ✓ | ✓ | ✓ | ✓ | ✓ | Portal-owned named object; notifications are linked separately. |
| `SUBSCRIPTION` | ✓ | — | — | — | ✓ | ✓ | — | Subscription syntax is sugar over Orchestrator job/schedule/notification metadata. |

## Examples

```sql
CREATE TABLE IF NOT EXISTS #stage (id INT);
CREATE OR REPLACE TABLE #stage (id INT, loaded_at DATE);

CREATE OR ALTER CONNECTION warehouse AS POSTGRES(HOST='pg01', DATABASE='dw');
DROP CONNECTION IF EXISTS warehouse;

CREATE OR REPLACE JOB Nightly FOR SCRIPT 'jobs/nightly.etlsql';
ALTER JOB Nightly ADD SCHEDULE NightlyTrigger;
DROP JOB IF EXISTS Nightly;

CREATE OR REPLACE VISUAL Revenue AS BAR (
  SOURCE = #sales,
  MAPPINGS (CATEGORY = region, VALUE = amount)
);
DROP VISUAL IF EXISTS Revenue;
```

References:
- [CREATE](ddl/create.md)
- [ALTER](ddl/alter.md)
- [DROP](ddl/drop.md)
- [Report-SQL Guide](../../guides/report-sql.md)
- [Job Orchestration](../orchestrator-jobs/schedule.md)
