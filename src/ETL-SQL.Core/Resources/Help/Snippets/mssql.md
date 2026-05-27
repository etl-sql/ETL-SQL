---
trigger: $mssql
label: CREATE CONNECTION … ON MSSQL
description: SQL Server or Azure SQL connection with Windows auth or SQL auth
---
CREATE CONNECTION «ConnName» AS MSSQL(
  SERVER             = '«server»',
  DATABASE           = '«database»',
  TRUSTED_CONNECTION = ON
);
