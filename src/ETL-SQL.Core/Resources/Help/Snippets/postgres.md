---
trigger: $postgres
label: CREATE CONNECTION … ON POSTGRES
description: PostgreSQL connection with host, database, user, and password
---
CREATE CONNECTION «ConnName» AS POSTGRES(
  HOST     = '«host»',
  PORT     = 5432,
  DATABASE = '«database»',
  USER     = '«username»',
  PASSWORD = '«password»'
);
