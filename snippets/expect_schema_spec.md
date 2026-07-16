---
trigger: $expect_schema_spec
label: EXPECT SCHEMA … FROM …
description: Validate schema using a JSON specification contract file
---
EXPECT SCHEMA «#table» FROM '«Specs/spec.json»'« ON DRIFT WARN»;
