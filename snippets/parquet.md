---
trigger: $parquet
label: CREATE CONNECTION … ON PARQUET
description: Apache Parquet columnar file connection
---
CREATE CONNECTION «ConnName» AS PARQUET(
  PATH        = '«path/to/file.parquet»',
  COMPRESSION = 'SNAPPY'
);
