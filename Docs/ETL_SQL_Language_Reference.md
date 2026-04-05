# ETL-SQL Language Reference

A powerful, high-performance ETL (Extract, Transform, Load) engine that blends the simplicity of **Standard SQL** with the flexibility of **Procedural Automation**. Designed for data engineers who need to orchestrate complex data flows without leaving the comfort of a SQL-first environment.

## Connection Management

Connectors define how the engine interacts with external data sources.

### CREATE CONNECTION
Defines a reusable connection to a source or destination platform. Connections must be defined before using them.

*Syntax:*
```sql
-- Traditional (String-based)
CREATE CONNECTION <name> ON <provider>('<connection_string>') [WITH(<options>)];

-- Structured (Property-based)
CREATE CONNECTION <name> ON <provider>() WITH(<properties...>, <options...>);
```

### Connection Types & Options

#### FLATFILE (or CSV)
For delimited text files.  
`CREATE CONNECTION <name> ON FLATFILE('<file_path>') [WITH(<options>)];`  
-- OR --  
`CREATE CONNECTION <name> ON FLATFILE() WITH(PATH='<file_path>', <options>);`

- **PATH**: The full path to the file. (Required in structured form)
- **HEADER**: `ON`, `OFF` (Default: `ON`).
- **DELIMITER**: `COMMA`, `PIPE`, `TAB`, `SEMICOLON`, `COLON`, `TILDE` or a literal `<char>` (Default: `COMMA`).
- **ROW_DELIMITER**: `LF`, `CR`, `CRLF`. `TILDE`, `SEMICOLON`, `COLON`, `COMMA`, `TAB`, `PIPE` or a literal `<char>` (Default: `CRLF`).
- **ENCODING**: `UTF8`, `ANSI`, `UTF16`, `LATIN1`, `UNICODE` (Default: `UTF8`).
- **TEXT_QUALIFIER**: `DOUBLEQUOTE`, `SINGLEQUOTE` or a literal `<char>`.
- **ESCAPE_CHAR**: Character used to escape delimiters within fields (e.g. `'\'`).
- **NULL_AS**: `NULL`, `EMPTY`, `BACKSLASH_N` — how null values are represented in the file.
- **DATE_FORMAT**: Custom date parsing format (e.g. `'yyyy-MM-dd'`).
- **START_AT**: Line number to start reading (1-based).
- **END_AT**: Line number to stop reading.
- **COUNT_AT_END**: `ON`, `OFF` — Validates row count at trailer. (Default: `OFF`)
- **STRICT_SCHEMA**: `ON`, `OFF` — enforces column count matching (Default: `OFF`).
- **COMPRESS**: `ON`, `OFF` — transparent GZip support. (Default: `OFF`)
- **ENCRYPT**: `ON`, `OFF` — AES encryption for the file. (Default: `OFF`)
- **PASSWORD**: Password for encryption/decryption. (Required if ENCRYPT=ON)    
- **ALGORITHM**: `MD5`, `SHA1`, `SHA2_256`, `SHA2_512` — algorithm to use for encryption/decryption. (Default: `SHA2_256`)
- **KEYFILE**: Path to the private key file for public-key authentication. (Required if ENCRYPT=ON)
- **PASSPHRASE**: The passphrase for the private key file (if any). (Required if ENCRYPT=ON)

#### MSSQL (or SQLSERVER)
For Microsoft SQL Server.
`CREATE CONNECTION <name> ON MSSQL('<connection_string>') [WITH(<options>)];`  
-- OR --  
`CREATE CONNECTION <name> ON MSSQL() WITH(SERVER='<server>', DATABASE='<database>', <options>);`

- **SERVER**: The server name or IP address for the MSSQL connection. (Required in structured form)
- **DATABASE**: The database name for the MSSQL connection. (Required in structured form)
- **TABLE**: Default table context.
- **TRUSTED_CONNECTION**: `TRUE` / `FALSE`. (Default: `FALSE`)
- **USER**: The username for the MSSQL connection. Do not use if TRUSTED_CONNECTION is set to TRUE.
- **PASSWORD**: The password for the MSSQL connection. Do not use if TRUSTED_CONNECTION is set to TRUE.
- **USE_SSL**: `TRUE` / `FALSE`. (Default: `FALSE`)
- **TRUST_SERVER_CERTIFICATE**: `TRUE` / `FALSE`. (Default: `FALSE`)
- Supports integrated security and T-SQL pushdown.

#### POSTGRES (or NPSQL)
For PostgreSQL.
`CREATE CONNECTION <name> ON POSTGRES('<connection_string>') [WITH(<options>)];`  
-- OR --  
`CREATE CONNECTION <name> ON POSTGRES() WITH(HOST='<host>', DATABASE='<database>', <options>);`

- **HOST**: The server name or IP address for the POSTGRES connection. (Required in structured form)
- **DATABASE**: The database name for the POSTGRES connection. (Required in structured form)
- **PORT**: Port for the connection. (Default: `5432`)
- **USER**: The username for the connection.
- **PASSWORD**: The password for the connection.
- **TABLE**: Default table context.
- Supports native SQL pushdown.

#### ORACLE
For Oracle Database.
`CREATE CONNECTION <name> ON ORACLE('<connection_string>') [WITH(<options>)];`  
-- OR --  
`CREATE CONNECTION <name> ON ORACLE() WITH(HOST='<host>', <options>);`

- **HOST**: The server name or IP address for the ORACLE connection. (Required in structured form)
- **PORT**: Port for the connection. (Default: `1521`)
- **SERVICE_NAME**: The Oracle service name.
- **TNS_NAME**: The Oracle TNS alias.
- **USER**: The username for the connection.
- **PASSWORD**: The password for the connection.
- **TABLE**: Default table context (e.g. `SCHEMA.TABLE`).
- Supports PL/SQL pushdown.

#### MOCKDB
An in-memory mock database designed for script development and testing. It requires no server, no credentials, and no configuration. The engine provides a set of pre-populated tables so you can write and test queries, joins, and ETL logic immediately.

`CREATE CONNECTION <name> ON MOCKDB();`  
*(No connection string or options are required.)*

**Pre-populated Tables:**

| Table | Columns |
| :---- | :------ |
| `Users` | `UserID`, `UserName`, `Email` |
| `Products` | `ProductID`, `ProductName`, `Price` |
| `Orders` | `OrderID`, `OrderDate`, `TotalAmount` |
| `Employee` | `ID`, `Name`, `Status`, `Active`, `first_name`, `last_name` |
| `departments` | `column1`, `column2`, `column3` |

- All tables are seeded with a small number of sample rows.
- Writes (`INSERT`, `UPDATE`, `DELETE`) are accepted but do not persist between sessions.
- Supports `EXECUTE...BEGIN...END` blocks with pass-through SQL execution against the mock tables.
- Supports `EXPLAIN`, schema introspection, and linting (treated as a database-type connector).

> [!NOTE]
> `MOCKDB` is intended for **development and testing only**. It should not be used in production scripts.

*Example:*
```sql
-- Develop and test a query without a real database
CREATE CONNECTION mock ON MOCKDB();

SELECT u.UserName, o.TotalAmount
INTO #result
FROM mock.Users AS u
JOIN mock.Orders AS o ON u.UserID = o.OrderID;

SELECT * FROM #result;

-- Test an EXECUTE block
EXECUTE mock INTO #emp
BEGIN
    SELECT ID, Name FROM Employee WHERE Active = 1;
END
```

#### PARQUET    
For Apache Parquet columnar files.
`CREATE CONNECTION <name> ON PARQUET('<file_path>') [WITH(<options>)];`  
-- OR --  
`CREATE CONNECTION <name> ON PARQUET() WITH(PATH='<file_path>', <options>);`

- **PATH**: The full path to the file. (Required in structured form)
- **COMPRESSION**: `SNAPPY` (Default), `GZIP`, `LZO`, `BROTLI`, `LZ4`, `ZSTD`, `UNCOMPRESSED`.

#### AVRO
For Apache Avro files.
`CREATE CONNECTION <name> ON AVRO('<file_path>') [WITH(<options>)];`  
-- OR --  
`CREATE CONNECTION <name> ON AVRO() WITH(PATH='<file_path>', <options>);`

- **PATH**: The full path to the file. (Required in structured form)
- **SCHEMA_FILE**: Path to a `.avsc` schema file.

#### EXCEL
For Excel file formats (.xlsx, .xls, .xlsb).
`CREATE CONNECTION <name> ON EXCEL('<file_path>') [WITH(<options>)];`  
-- OR --  
`CREATE CONNECTION <name> ON EXCEL() WITH(PATH='<file_path>', <options>);`

- **PATH**: The full path to the file. (Required in structured form)
- **SHEET**: Specific sheet name (Default: first sheet).
- **HEADER**: `ON`, `OFF` — treat first row as column headers (Default: `ON`).
- **RANGE**: Explicit cell range to read (e.g. `'A1:D100'`).
- **COMPRESS**: `ON`, `OFF` — GZip compress the output file.
- **ENCRYPT**: `ON`, `OFF` — AES encryption for the file.
- **PASSWORD**: Password for encryption/decryption.

#### JSON
For JSON data files.
`CREATE CONNECTION <name> ON JSON('<file_path>') [WITH(<options>)];`  
-- OR --  
`CREATE CONNECTION <name> ON JSON() WITH(PATH='<file_path>', <options>);`

- **PATH**: The full path to the file. (Required in structured form)
- **ROOT_PATH**: JSONPath to the data array (e.g. `$.Rows`, `$.data.items`).
- **COMPRESS**: `ON`, `OFF` — transparent GZip support.
- **ENCRYPT**: `ON`, `OFF` — AES encryption for the file.
- **PASSWORD**: Password for encryption/decryption.

#### XML
For XML data files.
`CREATE CONNECTION <name> ON XML('<file_path>') [WITH(<options>)];`  
-- OR --  
`CREATE CONNECTION <name> ON XML() WITH(PATH='<file_path>', <options>);`

- **PATH**: The full path to the file. (Required in structured form)
- **ROOT_PATH**: XPath to the repeating element (e.g. `/Catalog/Book`).
- **COMPRESS**: `ON`, `OFF` — transparent GZip support.
- **ENCRYPT**: `ON`, `OFF` — AES encryption for the file.
- **PASSWORD**: Password for encryption/decryption.

#### FTP (or FTP_CONN)
For remote file operations over FTP.
`CREATE CONNECTION <name> ON FTP('<host_address>') [WITH(<options>)];`  
-- OR --  
`CREATE CONNECTION <name> ON FTP() WITH(HOST='<host_address>', <options>);`

- **HOST**: The FTP server address or IP. (Required in structured form)
- **PORT**: The port for the FTP connection. (Default: `21`)
- **USER**: The username for the FTP connection.
- **PASSWORD**: The password for the FTP connection.
- Supports `GET_FILE`, `PUT_FILE`, and `REMOTE_FILE_LIST`.

#### SFTP (or SSH)
For remote file operations over SSH.
`CREATE CONNECTION <name> ON SFTP('<host_address>') [WITH(<options>)];`  
-- OR --  
`CREATE CONNECTION <name> ON SFTP() WITH(HOST='<host_address>', <options>);`

- **HOST**: The SSH server address or IP. (Required in structured form)
- **PORT**: The port for the SSH connection. (Default: `22`)
- **USER**: The username for the SSH connection.
- **PASSWORD**: The password for the SSH connection. Do not use if KEYFILE is set.
- **KEYFILE**: Path to the private key file for public-key authentication.
- **PASSPHRASE**: The passphrase for the private key file (if any).
- Supports `GET_FILE`, `PUT_FILE`, and `REMOTE_FILE_LIST`.

#### AZURE_BLOB (or BLOB)
For Azure Blob Storage.
`CREATE CONNECTION <name> ON AZURE_BLOB('<host_address_or_ip>') [WITH(<options>)];`  
-- OR --  
`CREATE CONNECTION <name> ON AZURE_BLOB() WITH(HOST='<host_address_or_ip>', <options>);`

- **CONTAINER**: The target blob container name.
- **ACCOUNT_NAME** / **ACCOUNT_KEY**: Storage credentials.
- Supports `GET_FILE`, `PUT_FILE`, and `REMOTE_FILE_LIST`.

#### SMTP (or EMAIL)
For sending emails.
`CREATE CONNECTION <name> ON SMTP('<host_address_or_ip>') [WITH(<options>)];`  
-- OR --  
`CREATE CONNECTION <name> ON SMTP() WITH(HOST='<host_address_or_ip>', <options>);`

- **PORT**: SMTP server port. (Default: `25`)
- **USERNAME** / **PASSWORD**: Authentication details.
- **USE_SSL**: `TRUE` / `FALSE`. (Default: `FALSE`)
- **DEFAULT_FROM**: Default sender email.
- Supports `SEND_EMAIL`.

#### DIRECTORY
Represents a local file system directory for listing files and performing filesystem operations.
`CREATE CONNECTION <name> ON DIRECTORY('<directory_path>') [WITH(<options>)];`  
-- OR --  
`CREATE CONNECTION <name> ON DIRECTORY() WITH(PATH='<directory_path>', <options>);`

- **CREATE**: `ON` / `OFF` — Create a new directory if it doesn't exist. (Default: `ON`)
- Supports `COPY_FILE`, `MOVE_FILE`, `DELETE_FILE`, and directory listing via `SELECT`.

*Examples:*
```sql
-- Delimited CSV file: Traditional
CREATE CONNECTION csv_in ON FLATFILE('C:\Data\employees.csv')
    WITH(HEADER=ON, DELIMITER=COMMA);

-- Delimited CSV file: Structured
CREATE CONNECTION csv_struct ON FLATFILE()
    WITH(PATH='C:\Data\employees.csv', HEADER=ON, DELIMITER=COMMA);

-- Encrypted and compressed flat file
CREATE CONNECTION secure_file ON FLATFILE('C:\Data\payroll.csv.gz')
    WITH(COMPRESS=ON, ENCRYPT=ON, PASSWORD='s3cr3t');

-- Excel workbook — specific sheet and range
CREATE CONNECTION xl_src ON EXCEL('C:\Reports\Q4.xlsx')
    WITH(SHEET='Summary', HEADER=ON, RANGE='A1:F500');

-- JSON file with nested data path
CREATE CONNECTION json_src ON JSON('C:\Data\orders.json')
    WITH(ROOT_PATH='$.orders');

-- XML file with XPath root
CREATE CONNECTION xml_src ON XML('C:\Data\catalog.xml')
    WITH(ROOT_PATH='/Catalog/Product');

-- Parquet with explicit compression
CREATE CONNECTION parquet_out ON PARQUET('C:\Data\output.parquet')
    WITH(COMPRESSION=SNAPPY);

-- SQL Server: Traditional (String-based)
CREATE CONNECTION db ON MSSQL('Server=myserver;Database=DW;Integrated Security=true')
    WITH(TABLE='dbo.Employees');

-- SQL Server: Structured (Property-based)
CREATE CONNECTION db_new ON MSSQL()
    WITH(SERVER='myserver', DATABASE='DW', TRUSTED_CONNECTION=TRUE, TABLE='dbo.Employees');

-- PostgreSQL
CREATE CONNECTION pg ON POSTGRES('Host=localhost;Database=mydb;Username=etl;Password=pass');

-- SFTP: Traditional
CREATE CONNECTION sftp_conn ON SFTP('sftp.example.com')
    WITH(USER='admin', PASSWORD='secret');

-- SFTP: Structured
CREATE CONNECTION sftp_struct ON SFTP()
    WITH(HOST='sftp.example.com', USER='admin', PASSWORD='secret');

-- SFTP with key-based auth
CREATE CONNECTION sftp_key ON SFTP('sftp.example.com')
    WITH(USER='deploy', KEYFILE='/home/etl/.ssh/id_rsa', PASSPHRASE='keypass');

-- FTP
CREATE CONNECTION ftp_conn ON FTP('ftp.example.com')
    WITH(USER='ftpuser', PASSWORD='ftppass');

-- Azure Blob Storage
CREATE CONNECTION cloud_store ON AZURE_BLOB('DefaultEndpointsProtocol=https;AccountName=myaccount;AccountKey=...')
    WITH(CONTAINER='backup');

-- Local directory
CREATE CONNECTION data_dir ON DIRECTORY('C:\Data\Incoming');

-- SMTP email
CREATE CONNECTION mailer ON SMTP('smtp.gmail.com')
    WITH(PORT=587, USERNAME='alerts@example.com', PASSWORD='apppassword', USE_SSL=TRUE);

-- Encrypted connection string
CREATE CONNECTION secure_db ON MSSQL('ENC:U2FsdGVkX1+...');
```

### DROP CONNECTION
Removes a previously defined connection from the current execution context.

*Syntax:*
`DROP CONNECTION [IF EXISTS] <connection_name>;`

*Example:*
```sql
DROP CONNECTION IF EXISTS remote_srv;
```

### ALTER CONNECTION
Modifies a previously defined connection.  Previous options are preserved unless explicitly changed.

*Syntax:*
`ALTER CONNECTION <connection_name> [WITH(<options below separated by commas>)];`

*Example:*
```sql
ALTER CONNECTION remote_srv WITH(PASSWORD='newpassword');
```
### CREATE OR ALTER CONNECTION
Creates a new connection or modifies an existing connection.  If the connection exists it will be rebuilt with the new options provided.  Previous options are not preserved.

*Syntax:*
`CREATE OR ALTER CONNECTION <connection_name> ON <connection_type>(<connection_string>) [WITH(<options below separated by commas>)];`

*Example:*
```sql
CREATE OR ALTER CONNECTION remote_srv ON MSSQL('Server=myserver;Database=DW;Integrated Security=true')
    WITH(TABLE='dbo.Employees');
```

### Session Management

#### CLEAR SESSION
Explicitly deletes all temporary files, recovery manifests, and encrypted state associated with the current session. This is recommended for security-critical scripts or to free up disk space after large data operations.

*Syntax:*
`CLEAR SESSION;`

*Example:*
```sql
-- Perform sensitive operations
...
-- Cleanup before exiting
CLEAR SESSION;
```

### USE DOCKER
Spins up a containerized database instance (MsSql, Postgres, or Oracle) for temporary orchestration or testing.  Needs to happen before a connection is created that uses it.

*Syntax:*
`USE DOCKER('<image_name>') [AS <alias>];`

*Built-in Variables:*
- `<alias>.CONNECTION_STRING`: Returns the dynamically generated connection string for the specified container.
- `DOCKER.CONNECTION_STRING`: Returns the connection string for the last started container (backward compatibility).

*Management Commands:*
- `<alias> CLOSE;`: Stops and disposes of the container.
- `<alias> STOP;`: Stops the running container (persists state).
- `<alias> START;`: Resumes a stopped container.
- `<alias> PAUSE;`: Pauses the container execution.
- `DOCKER CLOSE;`: Closes all active Docker containers.

*Example:*
```sql
-- Multiple containers with aliases
USE DOCKER('mcr.microsoft.com/mssql/server:2022-latest') AS dms;
DECLARE @conn varchar(500) = dms.CONNECTION_STRING;

USE DOCKER('postgres:15-alpine') AS dpost;
DECLARE @pconn varchar(500) = dpost.CONNECTION_STRING;

-- Management
dms STOP;
dms START;
dms CLOSE;
dpost CLOSE;
```

## Variables & State Management
You have the ability to query from multiple different source databases and files.  When using these different sources you have access to different processes and syntax.  Knowing your context will help to avoid issues.

### Walk thru
In the query below your context is the ETL-SQL engine.
```sql
CREATE CONNECTION c ON FLATFILE('C:\Users\chuck\scratch\ETL-SQL\TestData\test_categories.csv');
DECLARE @date datetime = '01/01/2026';
SELECT @date;
```
But what if I wanted to run that on the database?
```sql
CREATE CONNECTION m ON MSSQL('localhost');

EXECUTE m
BEGIN
    DECLARE @date datetime = '01/01/2026';
    SELECT @date;
END
```
Now everything inside the `EXECUTE <name> BEGIN...END` block is run from the context of the SQL Server Engine.

What if I want the results
```sql
CREATE CONNECTION m ON MSSQL('localhost');

EXECUTE m INTO #emp
BEGIN
    SELECT t.id, t.[name] FROM dbo.Employee AS t WHERE t.id > 1;
END
```
Now the ETL-SQL engine will run the query in the EXECUTE block and the results returned from that query will be stored in a temporary table called #emp in the ETL-SQL engine.  So the results (#emp) now live in the context of ETL-SQL and as such it can transform, join, load them back into other connections.

In this scenario #emp is a temporary table in the ETL-SQL engine.  It is not a table in the Sql Server database.  It is a table in the ETL-SQL engine's memory.

What if I need to pass parameters down to the database?
```sql
CREATE CONNECTION m ON MSSQL('localhost');
DECLARE @id INT = 1;
       ,@name VARCHAR(50) = 'John';

EXECUTE m INTO #emp WITH(@id, @name)
BEGIN
    SELECT t.id, t.[name] FROM dbo.Employee AS t WHERE t.id > ? AND t.[name] = ?;
END
```
This pushes the value of @id into the stored procedure as the first parameter.  The ? is a placeholder for the parameter.  You can add as many parameters as you need to.  They will be processed in order.

What if I need to do way harder ones?
```sql
CREATE CONNECTION m ON MSSQL('localhost');
DECLARE @id INT = 1;
       ,@name varchar(50) = 'John';
       ,@stmt varchar(2000)
SET @stmt = 'SELECT t.id, t.[name] FROM dbo.Employee AS t WHERE t.id > ' + @id + ' AND t.[name] = ''' + @name + ''';';

EXECUTE (
    @stmt
) AT m
```
Dynamic SQL is a powerful tool, but it can be dangerous.  It is important to use it with caution.  Note the above query could be written with parameters as shown in the previous example this was just shown as an example of how to use dynamic sql.

My query is really straight forward.  I just want to select from a table and get the results.
```sql
CREATE CONNECTION m ON MSSQL('localhost');

SELECT t.id, t.[name] INTO #temp FROM m.dbo.Employee AS t WHERE t.id > 1;
```
There is a shorthand you can use for simple select queries.  You just need to add the connection to the table name in the FROM/JOIN clause.  In the above example we have to assume it knows what database to connect to from either security or the connection string.  The ETL-SQL engine will then run this query against the Sql Server connection and connect to the dbo.Employee table.  It will then return the results to the ETL-SQL engine and store them in the #temp table.

Cool but the temp tables in the ETL-SQL engine do not have the correct data types.
```sql
CREATE CONNECTION m ON MSSQL('localhost');
CREATE TABLE #emp(
   id int
  ,name varchar(50)
);

INSERT INTO #emp (id, name)
SELECT t.id, t.[name] FROM m.dbo.Employee AS t WHERE t.id > 1;

-- OR
INSERT INTO #emp (id, name)
EXECUTE m
BEGIN
  SELECT t.id, t.[name] FROM dbo.Employee AS t WHERE t.id > 1;
END
```
You can explicitly define the columns you want to insert into the temp table.  This is a good practice because it will prevent errors if the source table changes.  You just have to make sure you have the same number of columns and the same data types or it will fail.

### Flow Control
**`EXECUTE` (Remote Execution)**
Executes SQL code on a remote connection. Supports capturing results into local tables and passing parameters for secure, parameterized execution.

*Connection Block Form:*
```sql
EXECUTE ds [INTO #target] [WITH (@param1, @param2, ...)]
BEGIN
  -- Remote SQL dialect (e.g. T-SQL for MSSQL)
  SELECT id, name FROM remote_table WHERE category_id = ?1 OR alt_id = ?1;
END;
```

*String Literal Form:*
```sql
EXECUTE (
  'SELECT id, name FROM remote_table WHERE category_id = ?1'
) AT ds [INTO #target] [WITH (@cat_id)];
```

*Key Parameters:*
- **INTO #table**: Streams results from the remote execution directly into a local ETL-SQL memory table.
- **WITH (@vars)**: Passes local variables to the remote server. 
  - **Sequential**: Use `?` placeholders in the remote SQL; variables are applied in the order they are listed.
  - **Indexed**: Use `?1`, `?2`, etc., to refer to specific parameters in the `WITH` list. This allows using the same parameter multiple times or referring to them out of order.
- **Note**: The string literal form sends raw SQL directly to the server, so it requires valid target server syntax and standard single-quote escaping (`''`).

**`PARALLEL`**
The `PARALLEL` keyword allows for concurrent execution of multiple statements or blocks. This is particularly useful for independent data streams that do not have inter-dependencies, such as loading multiple dimension tables simultaneously.

*Syntax:*
```sql
PARALLEL
BEGIN
    <statement_1>;
    <statement_2>;
    ...
END
```

*Key Characteristics:*
- **Non-blocking**: Statements within a `PARALLEL` block are fired concurrently.
- **Wait-all**: The execution engine waits for *all* statements in the block to complete before moving to the next statement outside the block.
- **Isolated Scopes**: Each branch typically operates on its own connection state to avoid race conditions, though global variables are accessible.

*Example:*
```sql
-- Load three independent tables in parallel
PARALLEL
BEGIN
    SELECT * INTO #Dimensions_Date FROM src_db.DateDim;
    SELECT * INTO #Dimensions_Product FROM src_db.ProductDim;
    SELECT * INTO #Dimensions_Store FROM src_db.StoreDim;
END

-- Sequential execution resumes here after all three above are finished
```

**`RUN SCRIPT`**
Executes another ETL-SQL script file, optionally passing parameters.
*Syntax:*
`RUN SCRIPT '<script_path>' [WITH (@param1 = val1, ...)];`

*Example:*
```sql
RUN SCRIPT 'sub_process.etlsql' WITH (@batchId = 1234, @env = 'PROD');
```

### Variable Management

#### DECLARE
Defines a variable. The data type is optional; if omitted, it defaults to `ANY`. Multiple variables can be declared in a single statement.
```sql
DECLARE @name STRING = 'Chuck';
DECLARE @id = 123; -- Optional type (defaults to ANY)
DECLARE @list LIST = [1, 2, 3], @count INT = 0;
```

#### SET
Assigns a value to an existing variable.
```sql
SET @name = 'Charles';
```

#### USE PASSWORD
Sets the master password for the current session. This password is used to encrypt and decrypt sensitive data within the script (e.g., connection strings marked with `ENC:`). It is **not** the password for individual file connectors (SFTP, FLATFILE, etc.).

When typed interactively the characters are masked as `*`. Use `SET SHOW_PASSWORD ON` to reveal the password in plain text output.

*Syntax:*
`USE PASSWORD = '<password>';`

*Example:*
```sql
USE PASSWORD = 'myMasterSecret';

-- Now this encrypted connection string can be decrypted by the engine
CREATE CONNECTION db ON MSSQL('ENC:U2FsdGVkX1+...');
```

#### SET SHOW_PASSWORD
Controls whether the password supplied by `USE PASSWORD` is shown in plain text in the console/messages output. Default is `OFF`.

*Syntax:*  
`SET SHOW_PASSWORD ON;`  
`SET SHOW_PASSWORD OFF;`

```sql
SET SHOW_PASSWORD ON;
USE PASSWORD = 'mySecret'; -- password visible in output
SET SHOW_PASSWORD OFF;     -- restore masked mode
```

### Supported Data Types
ETL-SQL supports a wide range of data types tailored for ETL operations, variable declarations, and table schemas. Below is a detailed breakdown of the supported types, their default formats, and cast behavior.

#### 1. Numeric Types
*   **Types**: `INT`, `INTEGER`, `BIGINT`, `SMALLINT`, `TINYINT`, `DECIMAL`, `NUMERIC`, `MONEY`, `FLOAT`, `REAL`, `DOUBLE`
*   **Accepted Literals**: `123`, `-45`, `3.14`
*   **Default Behavior**: Inferred from literals automatically. Computations on integers yield integers unless a decimal/float is involved.
*   **Cast Behavior**: `CAST('12.5' AS INT)` rounds or truncates depending on context. `CAST('3.14' AS DECIMAL)` converts accurately to a high-precision decimal.

#### 2. Temporal (Date & Time) Types
*   **Types**: `DATE`, `DATETIME`, `DATETIME2`, `TIMESTAMP`, `TIME`, `DATETIMEOFFSET`
*   **Accepted Literals**: `'2023-10-31'`, `'2023-10-31 15:30:00'`, `'15:30:00'`
*   **Default Behavior**: Parsed automatically if the string matches common ISO 8601 formatting, or by using explicit functions like `DATETIMEFROMPARTS`.
*   **Cast Behavior**: `CAST('2024-01-01' AS DATE)` parses the string into a `DateTime` struct. Casting a date back to a string uses the standard `yyyy-MM-dd HH:mm:ss` format unless otherwise specified via `FORMAT()`.

#### 3. Character & String Types
*   **Types**: `STRING`, `VARCHAR`, `NVARCHAR`, `TEXT`, `CHAR`
*   **Accepted Literals**: `'Hello World'`, `"ColumnName"` (for identifiers, though some engines permit double quotes for strings depending on dialect config)
*   **Default Behavior**: Uses Unicode by default (equivalent to `NVARCHAR` in MSSQL).
*   **Cast Behavior**: Any value can be cast to `STRING`. Nulls cast to `NULL`.

#### 4. Logical & Boolean Types
*   **Types**: `BIT`, `BOOLEAN`, `BOOL`
*   **Accepted Literals**: `TRUE`, `FALSE`, `1`, `0`
*   **Default Behavior**: Used naturally in `IF` statements and conditionals.
*   **Cast Behavior**: `CAST(1 AS BOOL)` -> `TRUE`. `CAST('true' AS BOOL)` -> `TRUE`. `CAST('yes' AS BOOL)` might throw a casting error depending on strictness.

#### 5. Binary
*   **Types**: `VARBINARY`, `BINARY`, `IMAGE`, `BLOB`
*   **Accepted Literals**: Typically generated via functions like `HASHBYTES` or reading from binary `FILE` connectors.
*   **Cast Behavior**: Casting to string typically yields a base64 or hex representation.

#### 6. Structured Types
*   **Types**: `JSON`, `XML`
*   **Default Behavior**: Can be parsed and manipulated using path extraction functions or loaded directly into relational tables via `SELECT * FROM JSON(...)`.

#### 7. Collections & Specialized Types
*   **`LIST`**: An ordered collection of values. Used with `IN` operators or traversed in `FOREACH` loops.
    ```sql
    DECLARE @myList LIST = (1, 2, 3);
    ```
*   **`PATH`**: Used specifically for file system paths or connection URIs. It ensures consistent handling of separators across different operating systems.
    ```sql
    DECLARE @sourcePath PATH = 'C:\Data\Source\';
    ```
*   **`ENCRYPTED`**: A semantic type for sensitive data such as connection strings, passwords, or API keys. 
    ```sql
    DECLARE @apiKey ENCRYPTED = 'ENC:U2FsdGVkX1+...';
    ```
*   **`UNIQUEIDENTIFIER` / `UUID`**: Represents a globally unique identifier (e.g. `NEWID()`).
*   **`ANY`**: The default type when no type is specified in a `DECLARE` statement. It allows for dynamic type inference based on the assigned value.
    ```sql
    DECLARE @id = 123; -- Implicitly ANY, inferred as INT
    ```


## Querying & Filtering
**`SELECT`**
Fetches, transforms, and projects data from an established connection or temporary memory table. 

*Supported clauses (Syntactic Order):*
- `DISTINCT`: When used after `SELECT`, filters out duplicate rows from the final result set.
- `TOP <expression>`: Caps the number of returned rows (placed after `SELECT`). Supports literal numbers and variables.
- `INTO <target>`: (ETL specific) Streams the results directly into a destination connection or memory table (tables prefixed with `#`).
- `FROM <target>`: The source table or connection name. Supports aliases (e.g., `FROM my_conn AS T1`).
- `[INNER | LEFT | RIGHT | FULL | LEFT SEMI | LEFT ANTI] [HASH | LOOP | MERGE] JOIN <target> ON <condition>`: Combines data from multiple sources. You can optionally force a specific execution algorithm (`HASH`, `LOOP`, or `MERGE`) to explicitly optimize streaming performance against massive datasets.
    - `LEFT SEMI JOIN`: Returns rows from the left table where a match exists in the right table.
    - `LEFT ANTI JOIN`: Returns rows from the left table where *no* match exists in the right table.
- `[CROSS | OUTER] APPLY (<subquery>) <alias>`: Correlated subquery join. Allows the subquery to refer to columns from the left side of the apply.
- `WHERE <condition>`: Filters rows based on standard logical evaluations.
- `GROUP BY <columns>`: Aggregates datasets based on unique column pairings.
- `GROUP BY ROLLUP(<columns>)`: Produces a result set with subtotals and a grand total. For `ROLLUP(a, b, c)`, generates groupings `(a,b,c)`, `(a,b)`, `(a)`, and `()` (grand total). Columns not in the current grouping are `NULL`.
- `GROUP BY CUBE(<columns>)`: Produces a result set for all possible combinations of the specified columns (2ⁿ groupings). Includes individual column subtotals and a grand total.
- `GROUP BY GROUPING SETS(<set1>, <set2>, ...)`: Explicitly specifies which groupings to compute. Each set is a parenthesized, comma-separated column list. An empty set `()` adds a grand-total row.
- `HAVING <condition>`: Filters result sets *after* GROUP BY aggregation has been applied.
- `PIVOT ( <aggregate_func>(<col>) FOR <pivot_col> IN (<values...>) ) AS <alias>`: Rotates a table-valued expression by turning unique values from one column in the expression into multiple columns in the output.
- `UNPIVOT ( <value_col> FOR <name_col> IN (<cols...>) ) AS <alias>`: Rotates a table-valued expression from a column-based form into a row-based form.
- `ORDER BY <column> [ASC|DESC] [, ...]`: Sorts the result set. Multiple columns are supported. `ASC` (default) or `DESC`. Can be used with `OFFSET`/`FETCH NEXT` for pagination.
- `OFFSET <expression> [ROWS]`: Skips a specific number of rows before returning results. Usually used with `ORDER BY`.
- `FETCH NEXT <expression> ROWS ONLY`: An alternative syntax for `LIMIT`, often used with `OFFSET`.
- `LIMIT <expression>`: Caps the number of returned rows (placed at the end of the query). Supports literal numbers and variables.
- `FOR JSON AUTO | PATH | RAW [, ROOT('name')] [, INCLUDE_NULL_VALUES] [, WITHOUT_ARRAY_WRAPPER]`: Formats results as a JSON string.
    - `AUTO`: Automatically determines the JSON hierarchy.
    - `PATH`: Provides full control over the JSON structure via column aliases (e.g., `col AS "Node.Child"`).
    - `RAW`: Returns each row as a single JSON object.
- `FOR XML AUTO | PATH | RAW [, ROOT('name')] [, ELEMENTS]`: Formats results as an XML string.
    - `AUTO`: Automatically determines the XML hierarchy.
    - `PATH`: Uses column aliases for nesting (e.g., `col AS "Node/Child"`).
    - `RAW`: Returns each row as a `<row>` element.
    - `ELEMENTS`: Formats column values as nested sub-elements instead of attributes (default).

*Example (standard query):*
```sql
SELECT DISTINCT
    category, 
    SUM(price) AS TotalSales,
    COUNT(DISTINCT transaction_id) AS UniqueItemsSold
INTO #sales_summary
FROM sales_db.transactions
WHERE created_at >= '2026-01-01'
GROUP BY category
HAVING TotalSales > @threshold
ORDER BY TotalSales DESC
LIMIT 10;
```

*Example (ROLLUP — subtotals and grand total):*
```sql
-- Produces per-region/product detail, per-region subtotals, and a grand total
SELECT Region, Product, SUM(Amount) AS Total
FROM #sales
GROUP BY ROLLUP(Region, Product)
ORDER BY Region, Product;
-- NULL in Region/Product marks the subtotal / grand-total rows
```

*Example (CUBE — all combinations):*
```sql
-- Subtotals by Region, by Product, and grand total
SELECT Region, Product, SUM(Amount) AS Total
FROM #sales
GROUP BY CUBE(Region, Product);
```

*Example (GROUPING SETS — explicit groupings):*
```sql
-- Only (Region+Product) detail and Region subtotals; no per-product or grand total
SELECT Region, Product, SUM(Amount) AS Total
FROM #sales
GROUP BY GROUPING SETS((Region, Product), (Region));
```

### Common Table Expressions (CTE)
CTEs provide a way to define temporary result sets that can be referenced within the scope of a single `SELECT`, `INSERT`, `UPDATE`, or `DELETE` statement. They improve readability and support recursive logic.

*Syntax:*
```sql
WITH [RECURSIVE] cte_name AS (
    <query_definition>
) [, ...]
<main_statement>;
```

*Example (Standard CTE):*
```sql
WITH HighSales AS (
    SELECT category, SUM(price) AS Total
    FROM sales_db.transactions
    GROUP BY category
)
SELECT * FROM HighSales WHERE Total > 10000;
```

*Example (Recursive CTE):*
```sql
-- Generate a sequence of numbers
WITH RECURSIVE Counter AS (
    SELECT 1 AS n
    UNION ALL
    SELECT n + 1 FROM Counter WHERE n < 10
)
SELECT n FROM Counter;
```

### PIVOT and UNPIVOT
*Example (PIVOT — rotate rows to columns):*
```sql
-- Sales per quarter, pivoted so each quarter becomes a column
SELECT category, [Q1], [Q2], [Q3], [Q4]
FROM (
    SELECT category, quarter, amount FROM #sales
) AS src
PIVOT (
    SUM(amount) FOR quarter IN ([Q1], [Q2], [Q3], [Q4])
) AS pvt;
```

*Example (UNPIVOT — rotate columns back to rows):*
```sql
-- Normalize Q1–Q4 columns back into rows
SELECT category, quarter, amount
FROM #quarterly_sales
UNPIVOT (
    amount FOR quarter IN ([Q1], [Q2], [Q3], [Q4])
) AS unpvt;
```

### Pagination
*Example (pagination with OFFSET / FETCH NEXT):*
```sql
SELECT id, name, amount
FROM #sales
ORDER BY amount DESC
OFFSET 20 ROWS
FETCH NEXT 10 ROWS ONLY;
```

### JSON and XML
*Example (FOR JSON):*
```sql
SELECT id, name, amount
FROM #sales
FOR JSON PATH, ROOT('Sales'), INCLUDE_NULL_VALUES;
```

## Logical Operators & Advanced Filters

Standard binary and logical operators are natively supported (`=`, `<`, `>`, `<=`, `>=`, `<>`, `AND`, `OR`).

**`IN`** and **`NOT IN`**
Checks a field's value against an explicit list or a `LIST` variable.
```sql
WHERE category IN ('A', 'B') 
   OR status NOT IN @validCats
```

**`LIKE`**
Matches strings against SQL-standard wildcard patterns (`%` for any string, `_` for any single character).
You can optionally use `ESCAPE '<char>'` to treat wildcard characters as literal characters.
```sql
WHERE email LIKE '%@gmail.com'
  AND url LIKE 'https://test\_%' ESCAPE '\'
```

**`EXISTS`** and **`NOT EXISTS`**
Evaluates whether a subquery returns any rows.
```sql
WHERE EXISTS (SELECT 1 FROM #temp WHERE id = main.id)
```

## Set Operations

Combine results from multiple queries.
- **`UNION ALL`**: Combines all rows from both queries.
- **`UNION`**: Combines rows and removes duplicates.
- **`EXCEPT`**: Returns rows from the first query that are not in the second.
- **`INTERSECT`**: Returns rows present in both queries.

## Data Manipulation Language (DML)

#### INSERT INTO
Injects evaluated query outputs directly into a target table or connection. 

*Syntax:*
`INSERT INTO <connection_or_table> [(cols...)] [OUTPUT clauses...] SELECT <columns...> FROM <source> [WHERE <condition>];`

*Example:*
```sql
INSERT INTO sales_db.archive (category, TotalSales)
OUTPUT INSERTED.category, INSERTED.TotalSales INTO #AuditLog
SELECT category, TotalSales 
FROM #sales_summary 
WHERE TotalSales < 1000;
```

#### UPDATE
Updates pre-existing datasets. Supports `OUTPUT` to capture before/after states via `DELETED` and `INSERTED` pseudo-tables.

*Syntax:*
`UPDATE <connection.table> SET <col> = <val> [OUTPUT clauses...] [WHERE <condition>];`

*Example:*
```sql
UPDATE sales_db.archive 
SET status = 'Closed' 
OUTPUT DELETED.status AS OldStatus, INSERTED.status AS NewStatus
WHERE created_at < '2020-01-01';
```

#### DELETE
Removes rows from a table.

*Syntax:*
`DELETE FROM <connection.table> [OUTPUT clauses...] [WHERE <condition>];`

#### MERGE (UPSERT)
Synchronizes data between a source and target by performing multiple DML actions (INSERT, UPDATE, DELETE) in a single statement. It is the core of incremental ETL processing.

*Syntax:*
```sql
MERGE INTO <target> [AS T]
USING <source_table_or_subquery> [AS S]
ON <match_condition>
[WHEN MATCHED [AND <condition>] THEN <action>]
[WHEN NOT MATCHED [BY TARGET] [AND <condition>] THEN <action>]
[WHEN NOT MATCHED BY SOURCE [AND <condition>] THEN <action>];
```

*Supported Actions:*
- `UPDATE SET col = val, ...`
- `INSERT (cols...) VALUES (vals...)`
- `DELETE`

*Example:*
```sql
MERGE INTO target_table AS T
USING (SELECT * FROM staging_table) AS S
ON T.id = S.id
WHEN MATCHED THEN 
    UPDATE SET name = S.name, updated_at = GETDATE()
WHEN NOT MATCHED THEN
    INSERT (id, name) VALUES (S.id, S.name)
WHEN NOT MATCHED BY SOURCE THEN
    DELETE;
```

#### BULK INSERT
Dramatically accelerates data loading directly by streaming payload binaries (such as CSVs) onto target databases or tables using strict destination schema adherence. Features advanced column-reordering maps natively.

*Syntax:*
`BULK INSERT <connection_or_table> [(target_cols...)] FROM '<file_path>' WITH (<options>);`

*Supported Options:*
- **FORMAT**: `CSV`, `PARQUET`, `AVRO`, `EXCEL` (Default: `CSV`).
- **BATCHSIZE**: Number of rows to commit per transaction (e.g., `10000`).
- **MAXERRORS**: Number of parsing errors allowed before the job fails (Default: `0`).
- **FIELDTERMINATOR**: Column separator (e.g., `','`, `'|'`, `'\t'`).
- **ROWTERMINATOR**: Row separator (e.g., `'\n'`, `'\r\n'`).
- **FIRSTROW**: The 1-based index of the first row to be imported (e.g., `2` to skip a header).
- **DATE_FORMAT**: Custom format for date fields.
- **STRICT_SCHEMA**: `ON`, `OFF` (Default: `ON`). If `OFF`, allows for extra columns or missing columns in the data.

*Example:*
```sql
BULK INSERT #Target (Name, Location, Age) 
FROM 'TestData\test_bulk_mapped.csv'
WITH (FORMAT = 'CSV');
```

## Statements
### DDL & Resource Management

#### CREATE TABLE
Defines a structure for storing data, including support for various constraints.

*Syntax:*
```sql
CREATE TABLE <table_name> (
    <col_name> <data_type> [IDENTITY] [PRIMARY KEY] [UNIQUE] [NOT NULL | NULL] [DEFAULT <expr>] [CHECK (<expr>)] [REFERENCES <ref_table>(<ref_col>)],
    ...
    [CONSTRAINT <name>] PRIMARY KEY (<cols...>),
    [CONSTRAINT <name>] UNIQUE (<cols...>),
    [CONSTRAINT <name>] CHECK (<expr>),
    [CONSTRAINT <name>] FOREIGN KEY (<cols...>) REFERENCES <ref_table>(<ref_cols...>)
);
```

*Example:*
```sql
CREATE TABLE Orders (
    OrderId INT IDENTITY PRIMARY KEY,
    OrderDate DATETIME DEFAULT GETDATE(),
    TotalAmount DECIMAL(18,2) NOT NULL CHECK (TotalAmount >= 0),
    CustomerId INT REFERENCES Customers(Id),
    Status VARCHAR(20) DEFAULT 'Pending'
);

-- Table-level multi-column constraint
CREATE TABLE OrderItems (
    OrderId INT,
    LineItem INT,
    PRIMARY KEY (OrderId, LineItem)
);
```

#### ALTER TABLE
Modifies an existing table's structure.

*Syntax:*
- `ALTER TABLE <name> ADD <col_definition>;`
- `ALTER TABLE <name> DROP COLUMN <col_name>;`
- `ALTER TABLE <name> RENAME COLUMN <old_name> TO <new_name>;`

#### DROP TABLE
Deletes a table from the engine's memory or the target data source.
```sql
DROP TABLE [IF EXISTS] table_name;
```

#### TRUNCATE TABLE
Efficiently removes all rows from a table. Faster than `DELETE` for large datasets.

*Syntax:*
`TRUNCATE TABLE <connection.table>;`

#### CREATE INDEX
Creates an index on a table to improve query performance.
*Syntax:*
`CREATE [UNIQUE] INDEX <index_name> ON <table_name> (<column_name> [ASC|DESC] [, ...]);`

#### DROP INDEX
Removes an existing index.
*Syntax:*
`DROP INDEX <table_name>.<index_name>;`

### Environment Sets

Environment sets let you define named groups of variable assignments that can be applied to the current session in one step. This is useful for switching between environments (e.g. DEV, QA, PROD) without changing the script logic.

#### CREATE SETS
Defines a named set of variable assignments. The `BEGIN...END` block contains comma-separated `@variable = value` assignments. The optional `SET WITH_PROMPT ON` directive causes `USE SETS` to ask for confirmation before applying the set in interactive mode.

*Syntax:*
```sql
CREATE SETS !<name>
BEGIN
    @variable1 = <expression>,
    @variable2 = <expression>
    [SET WITH_PROMPT ON;]
END
```

*Example:*
```sql
-- Define a DEV environment
CREATE SETS !DEV
BEGIN
    @server   = 'dev-db.internal',
    @database = 'DevWarehouse',
    @schema   = 'dbo'
END

-- Define a PROD environment with a safety prompt
CREATE SETS !PROD
BEGIN
    @server   = 'prod-db.internal',
    @database = 'ProdWarehouse',
    @schema   = 'dbo';
    SET WITH_PROMPT ON;
END
```

#### USE SETS
Applies a previously defined named set, assigning all of its variables to the current session. If the set was created with `SET WITH_PROMPT ON`, the engine will prompt for confirmation before applying it (in interactive mode). In batch/non-interactive mode, the set is applied automatically.

*Syntax:*
```sql
USE SETS !<name>;
```

*Example:*
```sql
DECLARE @server   VARCHAR(100);
DECLARE @database VARCHAR(100);
DECLARE @schema   VARCHAR(50);

CREATE SETS !DEV
BEGIN
    @server   = 'dev-db.internal',
    @database = 'DevWarehouse',
    @schema   = 'dbo'
END

-- Define a PROD environment with a safety prompt
CREATE SETS !PROD
BEGIN
    @server   = 'prod-db.internal',
    @database = 'ProdWarehouse',
    @schema   = 'dbo';
    SET WITH_PROMPT ON;
END

USE SETS !DEV;

-- @server, @database, and @schema are now set to the DEV values
SELECT @server, @database, @schema;
```

#### DROP SETS
Removes a named set from the current session.

*Syntax:*
```sql
DROP SETS [IF EXISTS] !<name>;
```

*Example:*
```sql
DROP SETS !DEV;
DROP SETS IF EXISTS !STAGING;  -- No error if it doesn't exist
```

*Full environment-switching example:*
```sql
DECLARE @conn VARCHAR(200);
DECLARE @mode VARCHAR(50) = 'unknown';

CREATE SETS !DEV
BEGIN
    @conn = 'Server=dev-db;Database=Dev;',
    @mode = 'development'
END

CREATE SETS !PROD
BEGIN
    @conn = 'Server=prod-db;Database=Prod;',
    @mode = 'production';
    SET WITH_PROMPT ON;
END

-- Switch to DEV (no prompt)
USE SETS !DEV;
CREATE CONNECTION src ON MSSQL(@conn);

-- Switch to PROD (will prompt in interactive mode)
USE SETS !PROD;
CREATE CONNECTION dest ON MSSQL(@conn);
```

### Control Flow

#### IF...ELSE
Conditional execution.
```sql
IF @val > 10
BEGIN
    PRINT 'High';
END
ELSE
BEGIN
    PRINT 'Low';
END
```

#### WHILE
Loop execution.
```sql
WHILE @i < 10
BEGIN
    SET @i = @i + 1;
    IF @i = 5 CONTINUE;
    IF @i = 8 BREAK;
END
```

#### BREAK / CONTINUE
Control the flow of a `WHILE` loop. `BREAK` exits the loop immediately, and `CONTINUE` skips to the next iteration.

#### TRY...CATCH
Error handling block.
```sql
BEGIN TRY
    -- potentially failing code
END TRY
BEGIN CATCH
    PRINT 'Error: ' + ERROR_MESSAGE();
END CATCH
```

#### RAISERROR / THROW
Manually raise an error.
```sql
RAISERROR('Custom error message', 16, 1);
-- or
THROW 50000, 'Error message', 1;
```

#### WAITFOR DELAY
Pauses execution for a specified duration.
```sql
WAITFOR DELAY '00:00:05'; -- Wait 5 seconds
WAITFOR DELAY '00:01:30'; -- Wait 1 minute 30 seconds
```

> [!NOTE]
> `WAITFOR TIME '<HH:MM:SS>'` (wait until a specific clock time) is not currently implemented. Use `WAITFOR DELAY` with a calculated duration instead.

### Data Movement & Transformation

#### EXECUTE / EXEC
Executes a stored procedure or a dynamic SQL block.
```sql
EXECUTE RemoteProc @param1 = 'val';
EXEC('SELECT * FROM Table') AT RemoteConn;
```

#### INSERT INTO
Appends data to a table.
```sql
INSERT INTO TargetTable SELECT * FROM SourceTable;
```

### Job & Profile Management

#### CREATE JOB
Schedules a script to run automatically on a repeating interval.

*Syntax:*
```sql
CREATE JOB <job_name> ON SCHEDULE EVERY <n> SECONDS|MINUTES|HOURS|DAYS [AT '<HH:MM>'] AS <statement>;
```

- **EVERY `<n>` SECONDS/MINUTES/HOURS/DAYS**: The interval between runs.
- **AT `'HH:MM'`**: Optional. Pin the job to a specific time of day (e.g., run daily at 02:00 midnight).
- **AS `<statement>`**: The ETL-SQL statement or block to execute. Typically a `RUN SCRIPT` or `BEGIN...END` block.

*Examples:*
```sql
-- Run a cleanup script every 30 minutes
CREATE JOB CleanupJob ON SCHEDULE EVERY 30 MINUTES AS
    RUN SCRIPT 'scripts/cleanup.etlsql';

-- Run a daily archive job at 2 AM
CREATE JOB NightlyArchive ON SCHEDULE EVERY 1 DAY AT '02:00' AS
BEGIN
    INSERT INTO archive_db.logs SELECT * FROM prod_db.logs WHERE log_date < DATEADD(DAY, -30, GETDATE());
    DELETE FROM prod_db.logs WHERE log_date < DATEADD(DAY, -30, GETDATE());
END;
```

#### DROP JOB
Removes a scheduled job.
```sql
DROP JOB [IF EXISTS] <job_name>;
```

#### SHOW JOBS
Lists all currently registered background jobs.
```sql
SHOW JOBS;
```

#### SHOW JOB HISTORY
Displays the execution history and status of scheduled jobs.
```sql
SHOW JOB HISTORY;           -- all jobs
SHOW JOB HISTORY NightlyArchive;  -- specific job
```

#### KILL JOB
Terminates a running background job by its ID.
```sql
KILL JOB 'job_id_123';
```

#### SET PROFILING
Enables or disables performance profiling for the session.
```sql
SET PROFILING ON;
-- run scripts
SET PROFILING OFF;
```

#### SHOW PROFILE
Displays the timing and resource usage for the most recently executed statements.
```sql
SHOW PROFILE;
```

## Functions
### Conditional Logic
**`CASE ... WHEN ... THEN ... ELSE ... END`**
Evaluates conditions sequentially to return specific projected outputs natively.
```sql
SELECT
    CASE 
        WHEN id < 1000 THEN 'Legacy'
        WHEN id >= 1000 THEN 'Modern'
        ELSE 'Unknown'
    END AS SystemType
FROM system_table;
```

### String Functions
- **`LEFT(string, n)`**, **`RIGHT(string, n)`**: Extracts `n` characters from either side.
- **`CHARINDEX(substring, string)`** (or **`INSTR(string, substring)`**): Finds the 1-based index position of a substring. Not: `CHARINDEX` takes `(sub, str)` while `INSTR` takes `(str, sub)`.
- **`REVERSE(string)`**: Reverses the string characters.
- **`COALESCE(val1, val2...)`**: Returns the first non-null value in a list.
- **`ISNULL(val1, val2)`**: Returns `val2` if `val1` is null.
- **`NULLIF(val1, val2)`**: Returns NULL if the two values are equal, otherwise returns the first value.
- **`CAST(expr AS type)`**: Converts an expression to a specific type (`INT`, `STRING`, `DECIMAL`, `DATE`).
- **`TRY_CAST(expr AS type)`**: Similar to `CAST`, but returns `NULL` if the conversion fails instead of throwing an error.
- **`STUFF(string, start, length, new_string)`**: Deletes a specified length of characters and inserts a new sequence of characters at a specified starting point.
- **`STRING_ESCAPE(text, type)`**: Escapes special characters in a string based on the specified type (e.g., `'json'`).
- **`STRING_SPLIT(string, separator)`**: Splits a string into a list of substrings based on a specified separator.
- **`ASCII(string)`**, **`UNICODE(string)`**: Returns the integer ASCII or Unicode code point of the first character of the input string.
- **`CHAR(int)`**: Converts an integer ASCII code to its character equivalent.
- **`FORMAT(value, format)`**: Formats a value (Date or Numeric) based on a .NET format string.
- **`PATINDEX(pattern, string)`**: Returns the starting position of the first occurrence of a pattern in a specified expression. Supports `%` and `_` wildcards.
- **`STR(float, [length], [decimal])`**: Returns character data converted from numeric data. The `length` is the total length (including decimal point and sign), and `decimal` is the number of places to the right of the decimal point.
- **`QUOTENAME(string, [quote_char])`**: Returns a Unicode string with the delimiters added to make the input string a valid SQL Server delimited identifier. Default is `[]`.
- **`TRANSLATE(string, from, to)`**: Returns the string provided as a first argument after some characters specified in the second argument are translated into a destination set of characters specified in the third argument.
- **`DATALENGTH(value)`**: Returns the number of bytes used to represent any expression.
- **`TO_STR(value)`**: A convenience alias for `CAST(value AS STRING)`.
- **`REPLICATE(string, count)`**: Repeats a string value a specified number of times.

### List Scalars
- **`LENGTH(list)`**: Returns the number of items in a list.
- **`SORT_LIST(list[, 'ASC'|'DESC'])`**: Returns a sorted version of the list.
- **`APPEND_TO_LIST(@list, value)`**: Adds an item to a list variable.
- **`REMOVE_FROM_LIST(@list, value)`**: Removes an item from a list variable.
- **`LPAD(str, len[, pad])`**, **`RPAD(str, len[, pad])`**: Pads a string to a specific length.
- **`INITCAP(str)`**: Capitalizes the first letter of each word.
- **`POSITION(sub IN str)`** or **`STRPOS(str, sub)`**: Returns the 1-based index of a substring.
- **`LEAST(v1, v2...)`**, **`GREATEST(v1, v2...)`**: Returns the smallest or largest value from a list.
- **`DECODE(val, search1, result1, [search2, result2, ...], default)`**: Oracle-style CASE shorthand.

### Math Scalars
- **`ROUND(numeric, length)`**: Rounds to the specified decimal precision.
- **`ABS(numeric)`**: Absolute value.
- **`CEILING(numeric)`**, **`FLOOR(numeric)`**: Rounds up or down to the nearest integer.
- **`POWER(base, exp)`**: Exponential calculation.
- **`SQRT(float)`**: Square root.
- **`RAND([seed])`**: Returns a random float between 0 and 1.
- **`SIN(float)`**, **`COS(float)`**, **`TAN(float)`**: Standard trigonometric functions (input in radians).
- **`ASIN(float)`**, **`ACOS(float)`**, **`ATAN(float)`**: Inverse trigonometric functions (returns radians).
- **`ATAN2(y, x)`**: Returns the angle in radians between the positive x-axis and the point (x, y).
- **`SIGN(numeric)`**: Returns the sign of a number: `1` for positive, `-1` for negative, and `0` for zero.

### Date and Time Scalars
- **`CURRENT_TIMESTAMP`, `GETDATE()`, `GETUTCDATE()`**: Functions returning current system time.
- **`DATEADD(datepart, number, date)`**: Adds a specific interval to a date.
- **`DATEDIFF(datepart, startdate, enddate)`**: Returns the difference between two dates.
- **`DATEPART(datepart, date)`**, **`DATENAME(datepart, date)`**: Extracts parts of a date.
- **`YEAR(date)`**, **`MONTH(date)`**, **`DAY(date)`**: Extract integer components from a date.
- **`EOMONTH(date[, months_to_add])`**: Returns the last day of the month containing the specified date.
- **`ISDATE(expr)`**: Validates if an expression can be converted to a valid date.
- **`DATEFROMPARTS(year, month, day)`**: Constructs a new date.
- **`DATETIMEFROMPARTS(year, month, day, hour, minute, second, ms)`**: Constructs a new datetime.
- **`TIMEFROMPARTS(hour, minute, second, fractions, precision)`**: Constructs a new time.
- **`DATETIMEOFFSETSFROMPARTS(...)`**: Constructs a datetime with offset.
- **`FORMAT(value, format)`**: Formats an object based on a C# `ToString` configuration string.
- **`TRUNC(date)`**, **`TO_DATE(date)`**: Truncates time portions from a datetime.
- **`expr AT TIME ZONE 'timezone_id'`**: Converts a datetime to the specified timezone. Default input kind is UTC if not specified.

### Cryptography & Hashing
- **`HASHBYTES('algorithm', value)`**: Returns a hash of the input using `MD5`, `SHA1`, `SHA2_256`, or `SHA2_512`.
- **`CHECKSUM(val1, val2...)`**: Returns a high-uniqueness numeric hash (based on truncated SHA-256) for the combined inputs.
- **`BINARY_CHECKSUM(val1, val2...)`**: Returns a binary-compatible hash.
- **`NEWID()`**: Returns a new unique identifier (GUID).
- **`NEWSEQUENTIALID()`**: Returns a new GUID optimized for sequential insertion.

### JSON Functions
ETL-SQL provides native support for parsing, querying, and modifying JSON data.
- **`JSON_VALUE(json, path)`**: Extracts a scalar value from a JSON string.
- **`JSON_QUERY(json, path)`**: Extracts an object or an array from a JSON string.
- **`JSON_MODIFY(json, path, value)`**: Updates the value of a property in a JSON string.
- **`ISJSON(json)`**: Validates whether a string contains valid JSON.
- **`JSON_EXISTS(json, path)`**: Tests whether a specific path exists in a JSON string.
- **`JSON_OBJECT(key:val, ...)`**: Constructs a JSON object from key-value pairs.
- **`JSON_ARRAY(val, ...)`**: Constructs a JSON array from a list of values.
- **`OPENJSON(json[, path])`**: (Table-valued) Parses JSON text and returns data as a table.
- **`JSON_EXTRACT(json, path)`**: Returns the data at the specified path (alias for `JSON_QUERY`).

### XML Functions
Comprehensive XML processing capabilities for structured data interchange.
- **`XMLVALUE(xml, xpath)`**: Extracts a scalar value using XPath.
- **`XMLEXISTS(xml, xpath)`**: Returns true if the XPath expression matches any nodes.
- **`XMLQUERY(xml, xpath)`**: Returns an XML fragment matching the XPath.
- **`XMLELEMENT(name, ...)`, `XMLATTRIBUTES(...)`, `XMLFOREST(...)`**: Constructs XML elements and structures.
- **`EXTRACTVALUE(xml, xpath)`**: Traditional function for extracting text from XML nodes.

### Regular Expressions (Regex)
Powerful pattern matching and manipulation using standard Regex syntax.
- **`REGEXP_LIKE(str, pattern)`**: Returns true if the string matches the pattern.
- **`REGEXP_SUBSTR(str, pattern)`**: Returns the substring that matches the pattern.
- **`REGEXP_REPLACE(str, pattern, replacement)`**: Replaces occurrences of the pattern.
- **`REGEXP_INSTR(str, pattern)`**: Returns the 1-based start position of the match.
- **`REGEXP_COUNT(str, pattern)`**: Returns the number of times the pattern occurs.
- **`REGEXP_MATCHES(str, pattern)`**: (Table-valued) Returns all matches as a result set.

### Additional Scalar & Math Functions
- **`EXP(n)`**, **`LOG(n)`**, **`LN(n)`**: Exponential and logarithmic functions.
- **`MOD(n, m)`**: Returns the remainder of `n/m`.
- **`NVL(val, default)`**, **`NVL2(val, if_not_null, if_null)`**: Oracle-style null handling.
- **`CUME_DIST()`, `PERCENT_RANK()`, `NTH_VALUE(col, n)`**: Advanced window and distribution functions.
- **`PERCENTILE_CONT(n)`, `PERCENTILE_DISC(n)`**: Statistical percentile calculations.

### File & Directory Introspection
Functions to query the state of the local or remote filesystem.
- **`FILE_EXISTS('path')`**: Returns true if the file exists.
- **`DIRECTORY_EXISTS('path')`**: Returns true if the directory exists.
- **`FILE_LIST('path'[, 'filter'])`**: Returns a table containing information about files in a directory.
- **`REMOTE_FILE_LIST(conn_name [, 'path'])`**: Returns a table of files from a remote connection (SFTP, FTP, or Azure Blob). The `conn_name` is the name of a configured remote connector.

```sql
-- List all files in an SFTP directory
SELECT * FROM REMOTE_FILE_LIST('my_sftp', '/uploads');

-- List files using a configured Azure Blob connection
SELECT FileName, Size FROM REMOTE_FILE_LIST('cloud_store', 'backups/');
```

### File Transfer
Transfers files between the local machine and a remote connector (SFTP, FTP, Azure Blob).

#### SEND_FILE
Uploads a local file to a remote connection.

*Syntax:*
`SEND_FILE '<local_path>', <connection_name>, '<remote_path>';`

```sql
SEND_FILE 'C:\Exports\report.csv', my_sftp, '/uploads/report.csv';
```

#### RECEIVE_FILE
Downloads a file from a remote connection to the local machine.

*Syntax:*
`RECEIVE_FILE <connection_name>, '<remote_path>', '<local_path>';`

```sql
RECEIVE_FILE my_sftp, '/data/input.csv', 'C:\Imports\input.csv';
```

### Email Operations

#### SEND_EMAIL
Sends an automated email (requires a configured `SMTP` connection).

*Syntax:*
```sql
SEND_EMAIL TO '<to_address>'
SUBJECT '<subject>'
BODY '<body>'
[CC '<cc_address>' [, '<cc2>', ...]]
[BCC '<bcc_address>' [, '<bcc2>', ...]]
[ATTACH '<file_path>' [, '<file2>', ...]]
[AT <smtp_connection>];
```

- **TO** *(required)*: Recipient email address.
- **SUBJECT** *(required)*: Email subject line.
- **BODY** *(required)*: Email body text.
- **CC**: One or more carbon-copy recipients.
- **BCC**: One or more blind carbon-copy recipients.
- **ATTACH**: One or more local file paths to attach.
- **AT**: The name of the `SMTP` connection to use. Required if no default SMTP connection is configured.

*Example:*
```sql
CREATE CONNECTION mailer ON SMTP('smtp.company.com')
    WITH(PORT=587, USERNAME='alerts@company.com', PASSWORD='secret', USE_SSL=TRUE);

SEND_EMAIL TO 'admin@company.com'
SUBJECT 'ETL Job Completed'
BODY 'All records processed successfully.'
CC 'manager@company.com'
ATTACH 'C:\Reports\summary.xlsx'
AT mailer;
```

### Aggregation
- **`COUNT([DISTINCT] col)`**: Aggregation tally. If `DISTINCT` is specified, only unique non-null values are counted.
- **`SUM(col)`**: Aggregation total.
- **`MIN(col)`**: Aggregation smallest value.
- **`MAX(col)`**: Aggregation highest value.
- **`AVG(col)`**: Aggregation mathematical mean.
- **`STRING_AGG(col, separator) [WITHIN GROUP (ORDER BY col [ASC|DESC])]`**: Concatenates values from multiple rows into a single string, separated by the specified string. Optionally orders the values before concatenation. NULL values are ignored.

## Window Functions

Window functions operate on a set of rows and return a single value for each row from the underlying query. The `OVER` clause defines the window or user-specified set of rows. The framing clause is optional and is used to define the window or user-specified set of rows.

### Window Framing (`ROWS` | `RANGE`)

Window functions support framing to restrict the rows within the partition used for calculation.
- `ROWS BETWEEN <start> AND <end>`: Specifies a physical offset from the current row.
- `RANGE BETWEEN <start> AND <end>`: Specifies a logical range based on values.

*Bounds:*
- `UNBOUNDED PRECEDING` / `FOLLOWING`
- `<n> PRECEDING` / `FOLLOWING`
- `CURRENT ROW`

```sql
SELECT 
    id, 
    amount,
    SUM(amount) OVER(ORDER BY id ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS running_total,
    AVG(amount) OVER(ORDER BY id ROWS BETWEEN 1 PRECEDING AND 1 FOLLOWING) AS moving_avg
FROM #sales;
```

### `COUNT(col) OVER (ORDER BY col [ASC|DESC])`
Counts the number of rows in the window.
```sql
SELECT 
    OrderID,
    OrderDate,
    TotalAmount,
    COUNT(OrderID) OVER (ORDER BY OrderDate) AS RunningCount
FROM Orders;
```

### `SUM(col) OVER (ORDER BY col [ASC|DESC])`
Calculates the running total of a column.
```sql
SELECT 
    OrderID,
    OrderDate,
    TotalAmount,
    SUM(TotalAmount) OVER (ORDER BY OrderDate) AS RunningTotal
FROM Orders;
```

### `MIN(col) OVER (ORDER BY col [ASC|DESC])`
Calculates the running minimum of a column.
```sql
SELECT 
    OrderID,
    OrderDate,
    TotalAmount,
    MIN(TotalAmount) OVER (ORDER BY OrderDate) AS RunningMin
FROM Orders;
```
### `MAX(col) OVER (ORDER BY col [ASC|DESC])`
Calculates the running maximum of a column.
```sql
SELECT 
    OrderID,
    OrderDate,
    TotalAmount,
    MAX(TotalAmount) OVER (ORDER BY OrderDate) AS RunningMax
FROM Orders;
```
### `AVG(col) OVER (ORDER BY col [ASC|DESC])`
Calculates the running average of a column.
```sql
SELECT 
    OrderID,
    OrderDate,
    TotalAmount,
    AVG(TotalAmount) OVER (ORDER BY OrderDate) AS RunningAvg
FROM Orders;
```

### `ROW_NUMBER() OVER (ORDER BY col [ASC|DESC])`
Assigns a unique sequential integer to each row within a partition, starting from 1.
```sql
SELECT 
    OrderID,
    OrderDate,
    TotalAmount,
    ROW_NUMBER() OVER (ORDER BY OrderDate) AS RowNum
FROM Orders;
```

### `RANK() OVER (ORDER BY col [ASC|DESC])`
Assigns a rank to each row within a partition based on the specified order. Rows with the same value receive the same rank, and the next rank is skipped (e.g., 1, 1, 3, 4).
```sql
SELECT 
    OrderID,
    OrderDate,
    TotalAmount,
    RANK() OVER (ORDER BY OrderDate) AS Rank
FROM Orders;
```
### `DENSE_RANK() OVER (ORDER BY col [ASC|DESC])`
Assigns a rank to each row within a partition based on the specified order. Rows with the same value receive the same rank, but the next rank is not skipped (e.g., 1, 1, 2, 3).
```sql
SELECT 
    OrderID,
    OrderDate,
    TotalAmount,
    DENSE_RANK() OVER (ORDER BY OrderDate) AS DenseRank
FROM Orders;
```

### `LAG(col[, offset[, default]])` and `LEAD(col[, offset[, default]])`
Accesses data from a previous or subsequent row in the same result set without the use of a self-join.
```sql
SELECT 
    OrderID,
    OrderDate,
    TotalAmount,
    LAG(TotalAmount, 1, 0) OVER (ORDER BY OrderDate) AS PreviousAmount,
    LEAD(TotalAmount, 1, 0) OVER (ORDER BY OrderDate) AS NextAmount
FROM Orders;
```
### `FIRST_VALUE(col)` and `LAST_VALUE(col)`
Returns the first or last value in an ordered set of values.
```sql
SELECT 
    OrderID,
    OrderDate,
    TotalAmount,
    FIRST_VALUE(TotalAmount) OVER (ORDER BY OrderDate) AS FirstAmount,
    LAST_VALUE(TotalAmount) OVER (ORDER BY OrderDate) AS LastAmount
FROM Orders;
```

### `NTILE(n)`
Distributes rows into `n` specified number of ranked groups (buckets). Rows are distributed as evenly as possible.
```sql
SELECT 
    OrderID,
    OrderDate,
    TotalAmount,
    NTILE(4) OVER (ORDER BY OrderDate) AS Quartile
FROM Orders;
```
## `CUME_DIST()`
Calculates the cumulative distribution of a value in a group of values.
```sql
SELECT 
    OrderID,
    OrderDate,
    TotalAmount,
    CUME_DIST() OVER (ORDER BY OrderDate) AS CumulativeDistribution
FROM Orders;
```
### `PERCENT_RANK()`
Calculates the relative rank of a row within a group of rows.
```sql
SELECT 
    OrderID,
    OrderDate,
    TotalAmount,
    PERCENT_RANK() OVER (ORDER BY OrderDate) AS PercentRank
FROM Orders;
```
### `NTH_VALUE(col, n)`
Returns the value of the `n`-th row in the window frame.
```sql
SELECT 
    OrderID,
    OrderDate,
    TotalAmount,
    NTH_VALUE(TotalAmount, 2) OVER (ORDER BY OrderDate) AS SecondAmount
FROM Orders;
```
### `PERCENTILE_CONT(n)` and `PERCENTILE_DISC(n)`
Statistical functions that calculate a percentile based on a continuous or discrete distribution. These require the `WITHIN GROUP (ORDER BY col [ASC|DESC])` clause.
```sql
SELECT 
    OrderID,
    OrderDate,
    TotalAmount,
    PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY TotalAmount) OVER (ORDER BY OrderDate) AS MedianAmount
FROM Orders;
```
```sql
SELECT 
    category,
    PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY price) OVER(PARTITION BY category) AS MedianPrice
FROM #products;
```


## Loops

Procedural loops are supported natively via C# execution within the ETL-SQL evaluator to handle repetition.

### `BEGIN ... END`
A Block wrapper that allows for multiple statements inside flow controls.

### `IF ... ELSE IF ... ELSE`
Branches data operations conditionally.
```sql
IF @amount > 1000
BEGIN
    INSERT INTO #high_value SELECT * FROM #sales WHERE amount > 1000;
END
ELSE IF @amount > 500
BEGIN
    INSERT INTO #mid_value SELECT * FROM #sales WHERE amount > 500;
END
ELSE
BEGIN
    INSERT INTO #low_value SELECT * FROM #sales;
END;
```

### `WHILE`
Repeats a statement or block while a specified condition is true.
```sql
DECLARE @accumulator INT = 0;
WHILE @accumulator < 5
BEGIN
SET @accumulator = @accumulator + 1;
END;
```

### `FOR`
Iterates a variable through a numeric range with an optional `STEP`.
```sql
FOR @idx = 100 TO 95 STEP -1
BEGIN
    INSERT INTO #results (loop_val) VALUES (@idx);
END;
```

### `FOREACH`
Iterates comprehensively through a designated `LIST` variable.
```sql
DECLARE @result_list LIST = [10, 20, 30];
FOREACH @val IN @result_list
BEGIN
    INSERT INTO #results (loop_val) VALUES (@val);
END;
```



## Modular ETL (Procedures & Functions)
### `CREATE PROCEDURE`
Defines a reusable block of ETL-SQL statements.
```sql
CREATE PROCEDURE ArchiveSales @olderThan DATE
AS
BEGIN
    INSERT INTO archive_db.sales SELECT * FROM sales_db.sales WHERE created_at < @olderThan;
    DELETE FROM sales_db.sales WHERE created_at < @olderThan;
END;

EXEC ArchiveSales '2025-01-01';
```

### `CREATE FUNCTION`
Defines a User-Defined Function (UDF) that returns a scalar value.
```sql
CREATE FUNCTION CalculateTax(@amount DECIMAL) RETURNS DECIMAL
AS
BEGIN
    RETURN @amount * 0.15;
END;

SELECT id, CalculateTax(price) AS tax FROM #sales;
```

#### DROP FUNCTION / PROCEDURE
Removes a user-defined function or procedure.
```sql
DROP FUNCTION [IF EXISTS] MyFunc;
DROP PROCEDURE [IF EXISTS] MyProc;
```

## Transactions & Error Handling

### Transactions
Supports atomic operations via a transaction stack.
- `BEGIN TRANSACTION` (or `BEGIN TRAN`)
- `COMMIT` (or `COMMIT TRAN`)
- `ROLLBACK` (or `ROLLBACK TRAN`)
- `@@TRANCOUNT`: Built-in variable returning the current nesting level.

### Error Handling
- `TRY...CATCH`: Standard block for capturing runtime exceptions.
- `THROW [number, 'message', state]`: Raises a custom error.

```sql
BEGIN TRY
    BEGIN TRANSACTION;
    -- Operations...
    IF @errorCondition = 1 THROW 50000, 'Custom Error', 1;
    COMMIT;
END TRY
BEGIN CATCH
    ROLLBACK;
    PRINT('Error: ' + ERROR_MESSAGE());
END CATCH;
```

## Automation 

### File & Directory Operations
Specialized commands for filesystem management. Supports **Connection-based Path Resolution** (e.g., `MyDir + '/file.csv'` where `MyDir` is a connection name).

- `COPY_FILE('src', 'dest')`
- `MOVE_FILE('src', 'dest')`
- `RENAME_FILE('src', 'new_name')`
- `DELETE_FILE('path')`
- `COMPRESS_FILE('src'[, 'dest'])`
- `ENCRYPT_FILE('src'[, 'dest'])` (Uses master password)
- `DECRYPT_FILE('src'[, 'dest'])`

### Directory Management
- `CREATE_DIRECTORY('path')`
- `DELETE_DIRECTORY('path')`
- `RENAME_DIRECTORY('path', 'new_name')`
- `MOVE_DIRECTORY('path', 'dest')`
- `COPY_DIRECTORY('src', 'dest')`
- `DELETE_DIRECTORY_CONTENTS('path')`

### Docker Operations
- `START_DOCKER <alias>`: Starts a Docker container for the specified connection.
- `STOP_DOCKER <alias>`: Stops a running Docker container.
- `PAUSE_DOCKER <alias>`: Pauses a running Docker container.
- `CLOSE_DOCKER <alias>`: Stops and removes a Docker container.

## Introspection

> [!NOTE]
> `SHOW CONNECTIONS`, `SHOW TABLES`, and `SHOW COLUMNS` are supported in the VS Code extension and console editor UI. They are not currently available as standalone SQL statements in batch scripts.

### `SHOW CONNECTIONS`
Displays all configured connections in the current session.

### `SHOW TABLES [connection_name]`
Displays all tables in a connection.

### `SHOW COLUMNS <connection_name>.<table_name>`
Displays all columns for a table via a specific connection.

### Utility Commands

#### `EXPLAIN`
Displays the execution plan for a query — showing the join strategies, index usage, and data flow before the query runs.

```sql
EXPLAIN
SELECT o.OrderId, c.Name
FROM orders_db.Orders AS o
JOIN customers_db.Customers AS c ON o.CustomerId = c.Id
WHERE o.Status = 'Open';
```

#### `HELP CONNECTION <type>`
Displays connection-specific options and supported operations for the given connector type.

```sql
HELP CONNECTION SFTP;
HELP CONNECTION MSSQL;
HELP CONNECTION FLATFILE;
```

#### `LINT`
Statically analyzes a script for errors and best practices without executing it. Returns a result set of findings.

*Syntax:*
`LINT ['<script_path>'];`

- With a path: analyzes the specified `.etlsql` file.
- Without a path: analyzes the current script (useful in interactive mode).

```sql
-- Analyze a script file
LINT 'scripts/nightly_load.etlsql';
```

#### `PRINT`
Outputs a message to the console or messages panel.

*Syntax:*
`PRINT(message[, show_timestamp[, format]]);`

```sql
PRINT('Starting job...');
PRINT('Load complete', TRUE);          -- with timestamp
PRINT(GETDATE(), TRUE, 'yyyy-MM-dd'); -- formatted date
```

### Job Scheduling

See [CREATE JOB](#create-job) in Job & Profile Management for the full scheduling syntax.

```sql
SHOW JOB HISTORY;               -- view all job runs
SHOW JOB HISTORY NightlyArchive; -- view a specific job's history
```

### `LINEAGE`
The lineage system provides end-to-end traceability of data movement, capturing every transformation, source-to-target mapping, and metadata tag inheritance.

*Syntax:*
- `LINEAGE(<target_table> [, <column_name>]) [TO '<file_path>'];`
- `SELECT * FROM LINEAGE(<target_table> [, <column_name>]);`

*Variants:*
1. **Console View**: `LINEAGE(#FinalAudit);` displays a hierarchical tree of sources in the console output.
2. **Column Trace**: `LINEAGE(#FinalAudit, 'UserId');` filters the trace to a single column's ancestry.
3. **Markdown Export**: `LINEAGE(#FinalAudit) TO 'reports/lineage.md';` generates a comprehensive Markdown report including a **Mermaid.js** relationship diagram and a detailed audit table.
4. **Queryable Source**: `SELECT Operation, SourceTables FROM LINEAGE(#FinalAudit);` allows you to treat the lineage audit log as a standard table for automated validation or reporting.

*Queryable Columns:*
- `Timestamp`, `Operation`, `TargetTable`, `TargetColumn`, `SourceTables`, `SourceColumns`, `Description`, `Metadata` (JSON), `DerivedFromDescriptions`, `SourceFile`, `Line`, `Column`.

### Metadata Inheritance & Amalgamation
When columns are combined or transformed (e.g., `UnitPrice * Qty AS Total`), the system automatically propagates metadata:
1. **Last-Seen Wins**: The primary description (`@d`) and individual custom tags are inherited from the last source column in the expression that has them defined.
2. **Amalgamation**: The `DerivedFromDescriptions` field is populated with a structured list of all involved source descriptions (e.g., `UnitPrice: Base price per unit, Qty: Number of items sold`), ensuring no context is lost during transformations.
3. **Global Persistence**: All tags assigned anywhere in the lineage chain are preserved and queryable at the final destination.

### Lineage Transformation Functions
- **`GET_TAGS(table_name [, column_name])`**: Returns a `LIST` of all custom metadata tag names defined for a table or specific column.
- **`GET_TAG_VALUE(table_name, column_name, tag_name)`**: Returns the string value of a specific metadata tag.

*Example:*
```sql
DECLARE @tags LIST = GET_TAGS('#TaggedUsers', 'UserId');
IF 'sensitive' IN @tags
BEGIN
    PRINT('Warning: Table contains sensitive data.');
END;
```

Attach arbitrary metadata tags to columns and tables using special comment blocks. Tags are captured by the lineage system for auditing, governance, and UI documentation. `@d:` is the reserved description tag.

**Column Tags** — placed immediately after a column expression in a `SELECT` list:
```
col_name /* @d: Description text; @tag: value; @another: value2; */
```

**Table Tags** — placed immediately after a table reference in `FROM` or `JOIN`:
```
FROM table_name /* @owner: TeamA; @sensitivity: high; */
```

*Example:*
```sql
SELECT 
    UserId  /* @d: Internal user ID; @sensitive: true; */,
    UserName /* @d: Full name of the user; @owner: Chuck; */
INTO #TaggedUsers
FROM m.Users /* @owner: SecurityTeam; */;
```