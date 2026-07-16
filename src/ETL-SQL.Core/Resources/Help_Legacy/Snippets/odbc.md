---
trigger: $odbc
label: CREATE CONNECTION … ON ODBC
description: ODBC connection via DSN or driver string for any ODBC-compatible source
---
CREATE CONNECTION «ConnName» AS ODBC(
  DSN      = '«MyDsn»',
  UID      = '«username»',
  PWD      = '«password»'
);
