# End-to-End Lineage Across Two Scripts (Flat File → EDW → Report)

A column in a report is worth little if nobody can say where its number came from. This recipe
traces one all the way back: a CSV lands in an EDW table through several transformations, and a
separate report script — run later, in its own session, with its own connection names — still shows
the CSV as the origin and every transformation applied in between.

The two halves are two scripts because that is the real shape of the problem. The load runs on a
schedule; the report is authored weeks later by someone who has never seen the load script. Lineage
crosses that boundary through an exported [OpenLineage](https://openlineage.io) document.

Both scripts ship and are exercised by the sample suite:
[`lineage_cookbook_01_edw_load.etlsql`](../../../samples/04_Orchestration/lineage_cookbook_01_edw_load.etlsql)
and
[`lineage_cookbook_02_report.rptsql`](../../../samples/04_Orchestration/lineage_cookbook_02_report.rptsql).
They use SQLite so the recipe runs with no infrastructure — the pattern is identical against SQL
Server or PostgreSQL.

**Requirements:** none beyond the engine. No server, no Docker, no credentials.

### Part 1 — the EDW load

```sql
CREATE CONNECTION pats AS FLATFILE('lineage_cookbook_patients.csv', HEADER = 'ON');
CREATE CONNECTION edw  AS SQLITE(DATABASE = 'output/lineage_cookbook_edw.db');

-- Start from a known state so the load is re-runnable.
EXECUTE edw BEGIN
    DROP TABLE IF EXISTS Patient;
    CREATE TABLE Patient (
        patient_id INTEGER PRIMARY KEY, full_name TEXT, birth_year INTEGER,
        state_code TEXT, admission_band TEXT
    );
END;

-- OpenLineage exports append — a run log accumulates — so clear the previous document.
BEGIN TRY
    DELETE FILE 'output/patient.openlineage.jsonl';
END TRY
BEGIN CATCH
    PRINT 'No previous lineage export to clear.';
END CATCH;

SELECT
    CAST(patient_id AS INT)                     /* @d: EDW surrogate key */ AS patient_id,
    TRIM(first_name) + ' ' + TRIM(last_name)    /* @d: Patient full name; @pii: true */ AS full_name,
    CAST(SUBSTRING(date_of_birth, 1, 4) AS INT) /* @d: Year of birth; @pii: true */ AS birth_year,
    UPPER(TRIM(state))                          /* @d: Normalized state code */ AS state_code,
    CASE
        WHEN CAST(admissions AS INT) = 0  THEN 'none'
        WHEN CAST(admissions AS INT) <= 2 THEN 'low'
        ELSE 'high'
    END                                         /* @d: Admission frequency band */ AS admission_band
INTO #patient_stage
FROM pats.FILE /* @source_system: Registration CSV; @load_pattern: full */;

INSERT INTO edw.Patient (patient_id, full_name, birth_year, state_code, admission_band)
SELECT patient_id, full_name, birth_year, state_code, admission_band FROM #patient_stage;

-- Hand the lineage to whoever reads this table next.
EXPORT LINEAGE FOR edw.Patient AS OPENLINEAGE TO 'output/patient.openlineage.jsonl';
```

**Validation** — the chain reads top to bottom, one row per hop per column:

```sql
SELECT step, operation, target_table, target_column, source_physical, transformation_kind
FROM eng.lineage
WHERE target_column IS NOT NULL;
```

| step | operation | target_table | target_column | source_physical | transformation_kind |
| ---: | :--- | :--- | :--- | :--- | :--- |
| 2 | SELECT INTO | `#patient_stage` | `full_name` | `FLATFILE …\lineage_cookbook_patients.csv` | StringOperation |
| 2 | SELECT INTO | `#patient_stage` | `birth_year` | `FLATFILE …\lineage_cookbook_patients.csv` | Cast |
| 2 | SELECT INTO | `#patient_stage` | `admission_band` | `FLATFILE …\lineage_cookbook_patients.csv` | CaseExpression |
| 3 | INSERT | `edw.Patient` | `full_name` | | PassThrough |

`source_physical` is the connection alias resolved to something that still means something outside
this script. `pats` is a name local to the file; `FLATFILE C:\…\patients.csv` is where the data
actually came from. Credentials are never included.

Transformations sit on the write that applied them — a `CAST` is never a hop of its own — so they
appear on the `SELECT INTO` that computed them, and the `INSERT` that follows is a pass-through.
Staging through `#patient_stage` is what gives each half its own row.

### Part 2 — the report, in a separate session

```sql
CREATE CONNECTION warehouse AS SQLITE(DATABASE = 'output/lineage_cookbook_edw.db');

-- Seed this session with what happened before it.
IMPORT LINEAGE FOR warehouse.Patient AS OPENLINEAGE FROM 'output/patient.openlineage.jsonl';

CREATE DATASET &patients_by_state AS (
    SELECT state_code, COUNT(*) AS patient_count,
           MIN(birth_year) AS earliest_birth_year, MAX(birth_year) AS latest_birth_year
    FROM warehouse.Patient
    GROUP BY state_code
);

CREATE VISUAL PatientsByState AS BAR (
    SOURCE = (SELECT state_code, patient_count FROM &patients_by_state),
    TITLE = 'Patients by State',
    MAPPINGS (X = state_code, Y = patient_count)
);
```

**Validation** — this session never opened the CSV, and the chain still starts there:

```sql
SELECT step, operation, target_table, target_column, source_tables, transformation_kind
FROM eng.lineage
WHERE target_column IS NOT NULL AND target_table <> 'RESULTSET';
```

| step | operation | target_table | target_column | transformation_kind |
| ---: | :--- | :--- | :--- | :--- |
| 2 | IMPORTED | `#patient_stage` | `full_name` | StringOperation |
| 2 | IMPORTED | `#patient_stage` | `admission_band` | CaseExpression |
| 3 | IMPORTED | `warehouse.Patient` | `full_name` | PassThrough |
| 4 | SELECT | `dataset:&patients_by_state` | `patient_count` | Aggregation |
| 5 | CREATE VISUAL | `report:PatientsByState` | `Y` | |

Read `source_tables` rather than `source_physical` on imported rows: an imported row already carries
the portable identity as its source name — that is what makes it portable — while `source_physical`
is the resolution of a connection alias in *this* session.

### The alias does not have to match

Part 1 called the connection `edw`; part 2 calls it `warehouse`. An alias is local to one script, so
export records the portable identity of the database instead, and import re-attaches whichever alias
the importing script uses for the same physical dataset. File datasets are matched on their full
path, because every file connector shares one `file://` namespace.

### Operational notes

- **Imports are a seed, not a freeze.** Imported rows carry the operation `IMPORTED`; anything the
  script records afterwards accrues on top, last-writer-wins. `DELETE LINEAGE FOR TABLE <table>`
  removes only imported rows — lineage captured by executing statements is immutable.
- **Exports append.** A run log accumulates by design. Part 1 deletes the previous document so the
  sample stays re-runnable; a production pipeline usually wants the accumulation, or a date-stamped
  file per run.
- **Encrypted connections resolve to nothing.** A connection declared with `ENC:` yields no physical
  identifier rather than a guess — the resolver reads the script, not the vault.
- **Sharing lineage without disclosing where it was read**: `SET NO_SAVE_CONNECTION = ON` omits the
  server from physical identifiers (`EDW.dbo.Patient` rather than `localhost:EDW.dbo.Patient`).
- **Cleanup**: delete `output/lineage_cookbook_edw.db` and `output/patient.openlineage.jsonl`.
  Part 1 recreates both.

See [LINEAGE](../../reference/statements/session-control/lineage.md) for the full grammar, the tag
catalog, and the `transformation_kind` values.
