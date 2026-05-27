---
trigger: $oracle
label: CREATE CONNECTION … ON ORACLE
description: Oracle Database connection via host/service or TNS alias
---
CREATE CONNECTION «ConnName» AS ORACLE(
  HOST         = '«host»',
  PORT         = 1521,
  SERVICE_NAME = '«ORCL»',
  USER         = '«username»',
  PASSWORD     = '«password»'
);
