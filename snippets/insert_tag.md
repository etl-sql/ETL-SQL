---
trigger: $insert_tag
label: INSERT TAG FOR TABLE … COLUMN …
description: Seed table or column tags explicitly before lineage inheritance runs
---
INSERT TAG FOR TABLE «TableName» COLUMN «ColumnName» (
  d = '«Column business definition»',
  owner = '«team_or_person»',
  steward = '«data_steward»',
  classification = '«internal»',
  pii = '«false»',
  quality = '«bronze»'
);
