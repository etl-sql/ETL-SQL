---
trigger: $dataset
label: CREATE DATASET &name
description: Shared, optionally cached report dataset referenced by multiple visuals
---
CREATE DATASET &«name» REFRESH EVERY '«1h»' AS (
  SELECT «col1», «col2»
  FROM «source»
  WHERE «condition»
);
