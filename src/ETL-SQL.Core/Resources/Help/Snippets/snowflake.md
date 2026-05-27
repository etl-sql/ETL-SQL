---
trigger: $snowflake
label: CREATE CONNECTION … ON SNOWFLAKE
description: Snowflake data warehouse connection with username/password or key-pair auth
---
CREATE CONNECTION «ConnName» AS SNOWFLAKE(
  HOST      = '«account.snowflakecomputing.com»',
  DATABASE  = '«database»',
  SCHEMA    = '«schema»',
  WAREHOUSE = '«warehouse»',
  USERNAME  = '«username»',
  PASSWORD  = '«password»'
);
