# IMPORT LINEAGE

Imports upstream OpenLineage metadata to connect provenance across decoupled script runs or batch pipelines, and removes imported seed records when needed.

## Syntax

```sql
-- Canonical import syntax
IMPORT LINEAGE FOR hospital.dbo.Patient AS OPENLINEAGE FROM 'exports/patient.openlineage.jsonl';

-- Optional AS OPENLINEAGE clause
IMPORT LINEAGE FOR hospital.dbo.Patient FROM 'exports/patient.openlineage.jsonl';

-- Import from a variable containing JSON
IMPORT LINEAGE FOR hospital.dbo.Patient FROM @lineage_json;

-- Remove imported seed lineage
DELETE LINEAGE FOR TABLE hospital.dbo.Patient;
```

## Semantics

- **Seed Provenance**: Imported rows carry the operation `IMPORTED`. They act as a baseline seed so downstream queries continue the historical lineage chain across script executions.
- **Alias Decoupling**: Lineage is matched on canonical connection URI (`mssql://host/db`) rather than local connection aliases, allowing different scripts to use different connection names.
- **Deletion Boundary**: `DELETE LINEAGE FOR TABLE` removes only imported seed rows; runtime lineage captured by executed statements is immutable.

## Examples

```sql
-- Script 2: Import lineage from Script 1 and continue pipeline
CREATE CONNECTION warehouse AS MSSQL('Server=localhost;Database=EDW;');
CREATE CONNECTION outfile AS FLATFILE(PATH='C:\tmp\output.csv');

-- Seed lineage from yesterday's upstream extraction
IMPORT LINEAGE FOR warehouse.dbo.Patient FROM 'exports/patient.openlineage.jsonl';

INSERT INTO outfile.FILE (name)
SELECT name FROM warehouse.dbo.Patient;

-- Lineage for outfile now traces: patients.csv -> EDW.dbo.Patient -> output.csv
```

## References

- [LINEAGE](lineage.md)
- [EXPORT LINEAGE](export-lineage.md)
- [Governance Tags](governance-tags.md)
- [Statement Reference](../README.md)
