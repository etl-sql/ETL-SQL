# ETL-SQL Development

## Tweaks & Minor Enhancements (Priority)

- [ ] **[Engine] Support `BigInteger` in `IsIntegerType` check**
  - File: [AggregateEngine.cs](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Engine/Engines/AggregateEngine.cs)
  - Detail: Include `System.Numerics.BigInteger` in the `IsIntegerType` type check so that high-precision integers mapping to `BigInteger` from Snowflake or BigQuery are correctly truncated on `AVG()` and other aggregation functions.

- [ ] **[Parser] Lexer/Parser Exception Sanitisation**
  - File: [Parser.cs](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Parser/Parser.cs) (or compile exception handlers)
  - Detail: Ensure that syntax exceptions or compilation error reports sanitize script source code context lines so that connection string credentials, keys, and `'ENC:...'` payloads are redacted before being printed to stdout or written to application logs.

## Future Pipeline Goals

- [ ] **[Engine] Pipeline Checkpoints / State Resume**
  - Detail: Implement native checkpoint management. When running multi-step ETL scripts, support a declarative checkpoint mechanism (e.g. `CHECKPOINT 'StepName'`) and utilize the SQLite job history catalog to allow failed runs to resume execution from the last successful checkpoint rather than restarting from step one.

- [ ] **[Connectors] First-Class Native MySQL/MariaDB Connector**
  - Detail: Introduce a native `MySqlConnector` provider client registration to eliminate ODBC bridge dependency and improve native dialect parsing and exception-wrapping for MySQL and MariaDB servers.
