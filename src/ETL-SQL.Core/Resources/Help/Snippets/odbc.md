---
trigger: $odbc
label: CREATE CONNECTION … ON ODBC
description: ODBC connection via DSN or driver string for any ODBC-compatible source
---
CREATE CONNECTION «ConnName» ON ODBC(
  DSN      = '«MyDsn»',
  UID      = '«username»',
  PWD      = '«password»'
);
