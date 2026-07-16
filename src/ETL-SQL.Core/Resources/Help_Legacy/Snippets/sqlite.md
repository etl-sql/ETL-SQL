---
trigger: $sqlite
label: CREATE CONNECTION … ON SQLITE
description: Local or in-memory SQLite relational database connection
---
CREATE CONNECTION «ConnName» AS SQLITE(
  DATABASE        = '«mydb.db»',
  TIMEOUT_SECONDS = «30»
);
