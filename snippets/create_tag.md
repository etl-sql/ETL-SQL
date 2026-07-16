---
trigger: $create_tag
label: CREATE TAG FOR TABLE … COLUMN …
description: Seed table or column tags explicitly before lineage inheritance runs
---
CREATE TAG FOR TABLE «TableName» COLUMN «ColumnName» (
  d = '«Column business definition»',
  owner = '«team_or_person»',
  steward = '«data_steward»',
  classification = '«internal»',
  pii = '«false»',
  quality = '«bronze»'
);
