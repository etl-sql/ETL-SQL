---
trigger: $expect
label: EXPECT … ON FAILURE … (column data-quality rule)
description: Validate column values inline and route failing rows to quarantine, a warning, or an error
---
«section_label»:
SELECT
  «key_column» EXPECT NOT NULL AND UNIQUE ON FAILURE QUARANTINE,
  «value_column» EXPECT «>= 0» ON FAILURE WARN
INTO «#clean_target»
FROM «source_table»
ON FAILURE QUARANTINE TO «quarantine_target» WITH (RETENTION = '30 DAYS')
ON FAILURE WARN;
