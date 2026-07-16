---
trigger: $avro
label: CREATE CONNECTION … ON AVRO
description: Apache Avro file connection with optional schema file
---
CREATE CONNECTION «ConnName» AS AVRO(
  PATH        = '«path/to/file.avro»',
  SCHEMA_FILE = '«path/to/schema.avsc»'
);
