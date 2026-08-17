# CREATE SETS
Defines a named, reusable group of variable assignments that can be activated as a unit to switch between environments (DEV, QA, PROD) or configuration profiles without editing connection strings or script logic.

## Syntax
```sql
CREATE SETS !SetName BEGIN
    @variable1 = 'value1',
    @variable2 = 'value2'
    [, @variableN = expression]
    [SET WITH_PROMPT ON;]
END;

USE SETS !SetName;

DROP SETS [IF EXISTS] !SetName;
```

## Options
- **!SetName** — The set identifier, always prefixed with `!`. Case-insensitive. Must be unique within the session.
- **@variable = value** — One or more variable assignments. Assignments may be separated by commas or semicolons. Values follow the same literal and expression rules as `DECLARE`.
- **SET WITH_PROMPT ON** — Optional guard placed inside the `BEGIN...END` block. When present, `USE SETS !SetName` prompts for user confirmation before applying the set. Use this for production environments to prevent accidental activation.

## Activation
```sql
USE SETS !SetName;
```
Activates the named set. All variables defined in the set become available to subsequent statements in the session. If a set with the same name was previously active, it is replaced.

## Cleanup
```sql
DROP SETS !SetName;
DROP SETS IF EXISTS !SetName;
```
Removes the named set definition from the session. `IF EXISTS` suppresses the error if the set does not exist.

## Examples

### Basic environment switching
```sql
CREATE SETS !DEV BEGIN
    @server   = 'dev-sql01',
    @database = 'Sales_Dev',
    @pwd      = 'devpass'
END;

CREATE SETS !PROD BEGIN
    @server   = 'prod-sql01',
    @database = 'Sales',
    @pwd      = 'ENC:U2Fs...=='
END;

-- Activate the desired environment
USE SETS !DEV;

CREATE CONNECTION db AS MSSQL(
    SERVER   = @server,
    DATABASE = @database,
    USER     = 'etluser',
    PASSWORD = @pwd
);

SELECT TOP 100 * FROM db.dbo.Orders;
```

### Production guard with SET WITH_PROMPT
```sql
CREATE SETS !PROD BEGIN
    @environment = 'PRODUCTION';
    @server      = 'prod-sql01';
    SET WITH_PROMPT ON;
END;

USE SETS !PROD;  -- engine prompts for confirmation before applying
```

### Shared environments via RUN SCRIPT
The recommended pattern for team scripts is to keep set definitions in a shared file and load them with `RUN SCRIPT`:

```sql
-- _environments.etlsql (shared, source-controlled)
CREATE SETS !DEV  BEGIN @server = 'dev-sql01',  @env = 'DEV'  END;
CREATE SETS !QA   BEGIN @server = 'qa-sql01',   @env = 'QA'   END;
CREATE SETS !PROD BEGIN @server = 'prod-sql01', @env = 'PROD'; SET WITH_PROMPT ON; END;
```

```sql
-- pipeline.etlsql
RUN SCRIPT '_environments.etlsql';
USE SETS !QA;

PRINT 'Running against: ' + @env;
```

### Multi-value set
```sql
CREATE SETS !Regions BEGIN
    @North = 'North',
    @South = 'South',
    @East  = 'East',
    @West  = 'West'
END;

USE SETS !Regions;

SELECT * FROM db.dbo.Sales WHERE Region = @North;
```

## Notes
- Set names are prefixed with `!` in all statements: `CREATE SETS !Name`, `USE SETS !Name`, `DROP SETS !Name`.
- Variable assignments inside the block use the same expression rules as `DECLARE` — literals, `ENC:` encrypted values, and scalar expressions are all valid.
- `USE SETS !name` **replaces** any previously active set of the same name; it does not merge.
- Sets can also be loaded from external `.sets` files. See `USE SETS` in [use.md](use.md) for details.
- `SET WITH_PROMPT ON` is only valid **inside** a `CREATE SETS` block; it is not a session-level setting.
- Encrypted values (`ENC:...`) inside a set require `USE PASSWORD` to be called before `USE SETS`.
- Sets are session-scoped and are not persisted across script runs unless re-declared or loaded via `RUN SCRIPT`.

## References
- [USE (USE SETS, USE PASSWORD)](use.md)
- [DECLARE](declare.md)
- [SET WITH_PROMPT](../set-commands/set-with-prompt.md)
- [Statement Reference — CREATE](../statements/ddl/create.md)
- [Statement Reference — DROP](../statements/ddl/drop.md)
- [Variables and Parameters](README.md)
