---
trigger: $expect_schema
label: EXPECT SCHEMA … ( … )
description: Validate that a table or result set has inline expected columns and types
---
EXPECT SCHEMA «#table» (
  «column_name» «VARCHAR»« NOT NULL»
);
