---
trigger: $tag_table
label: INSERT TAG FOR TABLE …
description: Table-level stewardship tags for lineage, catalog search, and governance policy
---
INSERT TAG FOR TABLE «TableName» (
  owner = '«team_or_person»',
  steward = '«data_steward»',
  contact = '«owner@example.com»',
  domain = '«Finance»',
  classification = '«internal»',
  quality = '«bronze»',
  source_system = '«source_system»',
  freshness = '«24h»',
  sla = '«available by 08:00 local time»',
  d = '«Table purpose and business definition»'
);
