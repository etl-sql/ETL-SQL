# ETL-SQL Data Connectors: Reference & Guide

Connectors define how the ETL-SQL engine interacts with external data sources. This document provides complete option references and instructional examples for every supported connector type.

---

## 1. Connection Syntax

Every connector supports two equivalent syntaxes. Use whichever is easiest to read, version-control, or encrypt.

### 1.1 Traditional (String-based)
The connection string is the primary argument. Ideal for native driver DSNs or encrypted (`ENC:…`) secrets.
```sql
CREATE CONNECTION <name> ON <type>('<connection_string>') [WITH(<options>)];
```

### 1.2 Structured (Property-based)
All parameters are passed explicitly inside `WITH()`. Recommended for readability and AI-assisted authoring.
```sql
CREATE CONNECTION <name> ON <type>() WITH(<properties>, <options>);
```

> [!TIP]
> Both forms produce identical results. Mix them on a per-connection basis; there is no performance difference.

### 1.3 Encrypted Connection Strings (`ENC:`)
Sensitive connection strings can be encrypted using the engine's master password. The engine detects the `ENC:` prefix automatically and decrypts the string before connecting.

```sql
-- Set the session master password first
USE PASSWORD = 'myMasterSecret';

-- The engine will decrypt this at connection time
CREATE CONNECTION secure_db ON MSSQL('ENC:U2FsdGVkX1+...');
```

> [!IMPORTANT]
> The `ENC:` prefix is handled entirely by the engine — connectors never see the encrypted string. Use the **ETL-SQL Encryptor** tool to encrypt strings using your master password.

---

## 2. Relational Database Connectors

SQL-capable connectors support pushdown: ETL-SQL executes operations natively on the remote server whenever possible, avoiding unnecessary data movement.

### 2.1 Microsoft SQL Server (`MSSQL`)
Aliases: `SQL`, `SQLSERVER`

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `SERVER` | Server name or IP address | Yes (structured) |
| `DATABASE` | Target database name | Yes (structured) |
| `USER` | SQL authentication username | No |
| `PASSWORD` | SQL authentication password | No |
| `TRUSTED_CONNECTION` | Use Windows Integrated Security (`TRUE`/`FALSE`) | No |
| `USE_SSL` | Enable SSL encryption for the connection (`TRUE`/`FALSE`) | No |
| `TRUST_SERVER_CERTIFICATE` | Bypass SSL certificate validation (`TRUE`/`FALSE`) | No |
| `APPLICATION_INTENT` | `READWRITE` or `READONLY` (for AG replicas) | No |
| `MULTI_SUBNET_FAILOVER` | Optimize failover for multi-subnet clusters (`TRUE`/`FALSE`) | No |
| `CONNECT_TIMEOUT` | Seconds to wait for a connection (Default: `15`) | No |
| `MIN_POOL_SIZE` | Minimum connections kept in the pool | No |
| `MAX_POOL_SIZE` | Maximum connections allowed in the pool | No |
| `POOL_LIFETIME` | Seconds before a pooled connection is recycled | No |
| `TABLE` | Default table context (e.g. `dbo.Employees`) | No |

> [!NOTE]
> Do not set `USER`/`PASSWORD` when using `TRUSTED_CONNECTION=TRUE`. They are mutually exclusive authentication methods.

*Examples:*
```sql
-- Standard SQL authentication
CREATE CONNECTION m_sales ON MSSQL()
    WITH(SERVER='sql01', DATABASE='SalesDB', USER='etl_worker', PASSWORD='s3cr3t');

-- Windows Integrated Security (traditional string)
CREATE CONNECTION m_hr ON MSSQL('Server=sql01;Database=HR;Trusted_Connection=True;');

-- Read-only replica with SSL
CREATE CONNECTION m_ro ON MSSQL()
    WITH(SERVER='sql01', DATABASE='DW', TRUSTED_CONNECTION=TRUE,
         APPLICATION_INTENT=READONLY, USE_SSL=TRUE, TRUST_SERVER_CERTIFICATE=TRUE);
```

---

### 2.2 PostgreSQL (`POSTGRES`)
Aliases: `NPSQL`, `PG`

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `HOST` | Server name or IP address | Yes (structured) |
| `DATABASE` | Target database name | Yes (structured) |
| `USER` | Login username | Yes |
| `PASSWORD` | Login password | Yes |
| `PORT` | Listening port (Default: `5432`) | No |
| `TABLE` | Default table context | No |
| `POOLING` | Enable connection pooling (`TRUE`/`FALSE`) | No |
| `MIN_POOL_SIZE` | Minimum pool size | No |
| `MAX_POOL_SIZE` | Maximum pool size | No |
| `CONNECTION_IDLE_LIFETIME` | Seconds before an idle connection is pruned | No |
| `SSL_MODE` | `DISABLE`, `PREFER`, `REQUIRE`, `VERIFY_CA`, `VERIFY_FULL` | No |
| `TRUST_SERVER_CERTIFICATE` | Bypass certificate validation (`TRUE`/`FALSE`) | No |

*Examples:*
```sql
-- Structured
CREATE CONNECTION pg_db ON POSTGRES()
    WITH(HOST='10.0.0.5', PORT=5432, DATABASE='inventory', USER='admin', PASSWORD='s3cr3t');

-- Traditional string
CREATE CONNECTION pg_legacy ON POSTGRES('Host=localhost;Database=mydb;Username=etl;Password=pass');
```

---

### 2.3 Oracle (`ORACLE`)
Oracle supports two patterns: **Service Name** (for direct connection) and **TNS** (for pre-configured aliases). They are mutually exclusive.

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `HOST` | Server name or IP | Yes (Service pattern) |
| `PORT` | Listening port (Default: `1521`) | No |
| `SERVICE_NAME` | Oracle service name | Yes (Service pattern) |
| `TNS_NAME` | Oracle TNS alias | Yes (TNS pattern) |
| `USER` | Login username | Yes |
| `PASSWORD` | Login password | Yes |
| `TABLE` | Default table context (e.g. `SCHEMA.TABLE`) | No |
| `POOLING` | Enable connection pooling (`TRUE`/`FALSE`) | No |
| `MIN_POOL_SIZE` | Minimum connections in the pool | No |
| `MAX_POOL_SIZE` | Maximum connections in the pool | No |
| `CONNECTION_LIFETIME` | Seconds a connection stays alive in the pool | No |

> [!CAUTION]
> `TNS_NAME` and `SERVICE_NAME` are **mutually exclusive**. Using both in the same connection will raise a parse error.

*Examples:*
```sql
-- Service Name pattern (structured)
CREATE CONNECTION o_dev ON ORACLE()
    WITH(HOST='oradb.local', PORT=1521, SERVICE_NAME='ORCL', USER='app_user', PASSWORD='pwd');

-- TNS Name pattern (traditional)
CREATE CONNECTION o_prod ON ORACLE('Data Source=MyTNS;User Id=app_user;Password=pwd;');
```

---

### 2.4 ODBC Bridge (`ODBC`)
Universal bridge for any source with a local ODBC driver. Supports both DSN-based and DSN-less connections. SQL pushdown depends on the underlying provider.

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `DSN` | Pre-configured Data Source Name | No |
| `DRIVER` | ODBC driver name in curly braces (e.g. `{SQLite3 ODBC Driver}`) | No |
| `SERVER` | Server name or IP | No |
| `PORT` | Listening port | No |
| `DATABASE` | Database name or file path | No |
| `UID` | Login username | No |
| `PWD` | Login password | No |
| `CONNECT_TIMEOUT` | Login timeout in seconds | No |

> [!NOTE]
> For DSN-less connections you must provide `DRIVER` and at least one identifying property (`SERVER` or `DATABASE`).

*Examples:*
```sql
-- DSN pattern
CREATE CONNECTION odbc_prod ON ODBC() WITH(DSN='ProdSales', UID='etl', PWD='pwd');

-- DSN-less SQLite
CREATE CONNECTION my_sqlite ON ODBC()
    WITH(DRIVER='{SQLite3 ODBC Driver}', DATABASE='C:\Data\local.db');
```

---

## 3. Flat File & Document Connectors

### 3.1 Flat Files (`FLATFILE`)
Aliases: `CSV`, `TSV`

General-purpose connector for delimited and fixed-width text files.

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `PATH` | Absolute path to the file | Yes (structured) |
| `DELIMITER` | Column separator: `COMMA`, `PIPE`, `TAB`, `SEMICOLON`, `COLON`, `TILDE`, or a literal char (Default: `COMMA`) | No |
| `ROW_DELIMITER` | Row separator: `LF`, `CR`, `CRLF`, or a literal char (Default: `CRLF`) | No |
| `HEADER` | `ON`/`OFF` — treat first row as column names (Default: `ON`) | No |
| `TEXT_QUALIFIER` | Quote character: `DOUBLEQUOTE`, `SINGLEQUOTE`, or a literal char | No |
| `ESCAPE_CHAR` | Character used to escape delimiters within fields (e.g. `'\\'`) | No |
| `ENCODING` | `UTF8`, `ANSI`, `UTF16`, `LATIN1`, `UNICODE` (Default: `UTF8`) | No |
| `CULTURE` | Locale for date/number parsing (e.g. `en-US`, `de-DE`) | No |
| `NULL_AS` | How nulls are represented: `NULL`, `EMPTY`, `BACKSLASH_N` | No |
| `DATE_FORMAT` | Custom date parsing pattern (e.g. `'yyyy-MM-dd'`) | No |
| `START_AT` | 1-based line number to start reading | No |
| `END_AT` | 1-based line number to stop reading | No |
| `TRIM` | `ON`/`OFF` — remove leading/trailing whitespace from fields | No |
| `COUNT_AT_END` | `ON`/`OFF` — validate row count against a trailer record (Default: `OFF`) | No |
| `STRICT_SCHEMA` | `ON`/`OFF` — enforce column count matching (Default: `OFF`) | No |
| `FORMAT` | `DELIMITED` (Default) or `FIXED` | No |
| `TEMPLATE` | Name of a `#temp` table defining fixed-width offsets (Required if `FORMAT=FIXED`) | Conditional |
| `COMPRESS` | `ON`/`OFF` — transparent GZip read/write (Default: `OFF`) | No |
| `ENCRYPT` | `ON`/`OFF` — AES file encryption (Default: `OFF`) | No |
| `PASSWORD` | Password for encryption/decryption (Required if `ENCRYPT=ON`) | Conditional |
| `ALGORITHM` | Hash algorithm: `MD5`, `SHA1`, `SHA2_256`, `SHA2_512` (Default: `SHA2_256`) | No |
| `KEYFILE` | Path to private SSH key for key-pair encryption | Conditional |
| `PASSPHRASE` | Passphrase for the key file | Conditional |

Querying a `FLATFILE` connection via `SELECT` the table name is `FILE` and the columns are named based on the header row in the file or if there is no header row then the columns are named `Column1`, `Column2`, ...

*Examples:*
```sql
-- Pipe-delimited with explicit encoding
CREATE CONNECTION csv_in ON FLATFILE()
    WITH(PATH='C:\Data\employees.csv', HEADER=ON, DELIMITER=PIPE, ENCODING=UTF8);

-- Encrypted and GZip-compressed
CREATE CONNECTION secure_file ON FLATFILE('C:\Data\payroll.csv.gz')
    WITH(COMPRESS=ON, ENCRYPT=ON, PASSWORD='s3cr3t');

-- European locale with semicolon delimiter and custom date format
CREATE CONNECTION eu_data ON FLATFILE('C:\Data\german_sales.csv')
    WITH(DELIMITER=SEMICOLON, CULTURE='de-DE', DATE_FORMAT='dd.MM.yyyy');

-- Skip header and first 2 data rows, stop at row 1000
CREATE CONNECTION paged ON FLATFILE('C:\Data\big.csv')
    WITH(HEADER=ON, START_AT=3, END_AT=1000);
```

#### Fixed-Width Files

To read a fixed-width file, define a template table that specifies the width of each field. The engine slices each line using the declared widths.

**Width rules:**
- `VARCHAR(N)` / `CHAR(N)` / `NVARCHAR(N)` — engine uses their `N` as the field width automatically.
- `/* @width: N */` metadata comment — explicitly overrides the data type width.

```sql
-- 1. Define the layout
CREATE TABLE #EmpLayout (
    ID      INT          /* @width: 5 */,
    Name    VARCHAR(20),          -- width = 20 from VARCHAR length
    Dept    CHAR(3),              -- width = 3 from CHAR length
    Active  BIT          /* @width: 1 */
);

-- 2. Create the connection
CREATE CONNECTION fixed_emp ON FLATFILE('employees.dat')
    WITH(FORMAT='FIXED', TEMPLATE=#EmpLayout, HEADER=OFF, TRIM=ON);

-- 3. Query as normal
SELECT * FROM fixed_emp;
```

> [!IMPORTANT]
> When `FORMAT='FIXED'`, the `TEMPLATE` option is mandatory. The engine raises an error if any column width cannot be determined.

---

### 3.2 Excel (`EXCEL`)
Aliases: `XLSX`, `XLS`

Reads and writes Microsoft Excel workbooks (`.xlsx`, `.xls`, `.xlsb`).

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `PATH` | Absolute path to the workbook | Yes (structured) |
| `SHEET` | Target sheet name (Default: first sheet) | No |
| `HEADER` | `ON`/`OFF` — treat first row as column names (Default: `ON`) | No |
| `RANGE` | Explicit cell range to read (e.g. `'A1:F500'`) | No |
| `COMPRESS` | `ON`/`OFF` — GZip the output file after writing | No |
| `ENCRYPT` | `ON`/`OFF` — AES file encryption (Default: `OFF`) | No |
| `PASSWORD` | Password for encryption/decryption (Required if `ENCRYPT=ON`) | Conditional |
| `ALGORITHM` | `MD5`, `SHA1`, `SHA2_256`, `SHA2_512` (Default: `SHA2_256`) | No |
| `KEYFILE` | Path to private SSH key for key-pair encryption | Conditional |
| `PASSPHRASE` | Passphrase for the key file | Conditional |

Querying a `EXCEL` connection via `SELECT` the table name is `FILE` and the columns are named based on the header row in the file or if there is no header row then the columns are named `Column1`, `Column2`, ...

*Examples:*
```sql
-- Specific sheet and range
CREATE CONNECTION xl_src ON EXCEL('C:\Reports\Q4.xlsx')
    WITH(SHEET='Summary', HEADER=ON, RANGE='A1:F500');

-- Write an encrypted workbook
CREATE CONNECTION xl_out ON EXCEL()
    WITH(PATH='C:\Secure\payroll.xlsx', ENCRYPT=ON, PASSWORD='safe_pass');
```

---

### 3.3 JSON (`JSON`)
Document extraction with JSONPath addressing for nested data.

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `PATH` | Absolute path to the file | Yes (structured) |
| `ROOT_PATH` | JSONPath to the root data array (e.g. `$.data.orders`) | No |
| `ENCODING` | Character encoding (Default: `UTF8`) | No |
| `COMPRESS` | `ON`/`OFF` — transparent GZip support | No |
| `ENCRYPT` | `ON`/`OFF` — AES file encryption (Default: `OFF`) | No |
| `PASSWORD` | Password for encryption/decryption (Required if `ENCRYPT=ON`) | Conditional |
| `ALGORITHM` | `MD5`, `SHA1`, `SHA2_256`, `SHA2_512` (Default: `SHA2_256`) | No |
| `KEYFILE` | Path to private SSH key for key-pair encryption | Conditional |
| `PASSPHRASE` | Passphrase for the key file | Conditional |

Querying a `JSON` connection via `SELECT` the table name is `FILE`.

*Examples:*
```sql
-- Drill into a nested array
CREATE CONNECTION json_src ON JSON('C:\Data\orders.json') WITH(ROOT_PATH='$.data.orders');

-- Compressed JSON
CREATE CONNECTION json_gz ON JSON() WITH(PATH='C:\Data\events.json.gz', COMPRESS=ON);
```

---

### 3.4 XML (`XML`)
Document extraction with XPath addressing for nested elements.

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `PATH` | Absolute path to the file | Yes (structured) |
| `ROOT_PATH` | XPath to the repeating element (e.g. `/Catalog/Book`) | No |
| `ENCODING` | Character encoding (Default: `UTF8`) | No |
| `COMPRESS` | `ON`/`OFF` — transparent GZip support | No |
| `ENCRYPT` | `ON`/`OFF` — AES file encryption (Default: `OFF`) | No |
| `PASSWORD` | Password for encryption/decryption (Required if `ENCRYPT=ON`) | Conditional |
| `ALGORITHM` | `MD5`, `SHA1`, `SHA2_256`, `SHA2_512` (Default: `SHA2_256`) | No |
| `KEYFILE` | Path to private SSH key for key-pair encryption | Conditional |
| `PASSPHRASE` | Passphrase for the key file | Conditional |

Querying a `XML` connection via `SELECT` the table name is `FILE`.

*Examples:*
```sql
-- XPath root selector
CREATE CONNECTION xml_src ON XML('C:\Data\catalog.xml') WITH(ROOT_PATH='/Catalog/Product');

-- Encrypted XML archive
CREATE CONNECTION xml_vault ON XML()
    WITH(PATH='C:\Vault\archive.xml', ENCRYPT=ON, PASSWORD='vault_pass');
```

---

### 3.5 Parquet (`PARQUET`)
Apache Parquet columnar format. Ideal for high-throughput analytics and interoperability with Spark, Hive, and data lake systems.

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `PATH` | Absolute path to the file | Yes (structured) |
| `COMPRESSION` | `SNAPPY` (Default), `GZIP`, `LZO`, `BROTLI`, `LZ4`, `ZSTD`, `UNCOMPRESSED` | No |
| `ENCRYPT` | `ON`/`OFF` — AES file encryption (Default: `OFF`) | No |
| `PASSWORD` | Password for encryption/decryption (Required if `ENCRYPT=ON`) | Conditional |
| `ALGORITHM` | `MD5`, `SHA1`, `SHA2_256`, `SHA2_512` (Default: `SHA2_256`) | No |
| `KEYFILE` | Path to private SSH key for key-pair encryption | Conditional |
| `PASSPHRASE` | Passphrase for the key file | Conditional |

*Examples:*
```sql
-- Write a Snappy-compressed Parquet file (default)
CREATE CONNECTION pq_out ON PARQUET() WITH(PATH='C:\Data\output.parquet');

-- Maximum compression for archival
CREATE CONNECTION pq_archive ON PARQUET('C:\Archive\data.parquet') WITH(COMPRESSION=ZSTD);
```

---

### 3.6 Avro (`AVRO`)
Apache Avro format. Schema is embedded within the file. Optionally reference an external `.avsc` schema file.

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `PATH` | Absolute path to the file | Yes (structured) |
| `SCHEMA_FILE` | Path to an external `.avsc` Avro schema file | No |
| `ENCRYPT` | `ON`/`OFF` — AES file encryption (Default: `OFF`) | No |
| `PASSWORD` | Password for encryption/decryption (Required if `ENCRYPT=ON`) | Conditional |
| `ALGORITHM` | `MD5`, `SHA1`, `SHA2_256`, `SHA2_512` (Default: `SHA2_256`) | No |
| `KEYFILE` | Path to private SSH key for key-pair encryption | Conditional |
| `PASSPHRASE` | Passphrase for the key file | Conditional |

*Examples:*
```sql
-- Read Avro with an external schema definition
CREATE CONNECTION avro_src ON AVRO('C:\Data\events.avro')
    WITH(SCHEMA_FILE='C:\Schemas\events.avsc');
```

---

## 4. Remote & Cloud Protocol Connectors

### 4.1 SFTP / SSH (`SFTP`)
Aliases: `SSH`

Secure File Transfer Protocol over SSH. Supports password and key-pair authentication (mutually exclusive).

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `HOST` | Server domain or IP address | Yes (structured) |
| `PORT` | Listening port (Default: `22`) | No |
| `USER` | Login username | Yes |
| `PASSWORD` | Login password — use for password auth only | No |
| `KEYFILE` | Path to the private SSH key — use for key auth only | No |
| `PASSPHRASE` | Passphrase for the private key (if set) | No |

> [!CAUTION]
> `PASSWORD` and `KEYFILE` are mutually exclusive. Providing both will cause an authentication error.

*Examples:*
```sql
-- Password authentication
CREATE CONNECTION sftp_pwd ON SFTP()
    WITH(HOST='sftp.example.com', USER='admin', PASSWORD='s3cr3t');

-- Key-pair authentication (recommended for production)
CREATE CONNECTION sftp_key ON SFTP('sftp.example.com')
    WITH(USER='deploy', KEYFILE='/home/etl/.ssh/id_rsa', PASSPHRASE='keypass');
```

---

### 4.2 FTP (`FTP`)
Aliases: `FTP_CONN`

Legacy File Transfer Protocol. Supports active and passive mode depending on the server.

> [!NOTE]
> `FTPS` (FTP over SSL/TLS) is treated as an alias token at parse time but uses the same connector. Provide `USE_SSL=TRUE` in the connection string if your server requires implicit FTPS.

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `HOST` | FTP server address or IP | Yes (structured) |
| `PORT` | Listening port (Default: `21`) | No |
| `USER` | Login username | No |
| `PASSWORD` | Login password | No |

*Examples:*
```sql
-- Structured
CREATE CONNECTION ftp_src ON FTP() WITH(HOST='ftp.example.com', USER='ftpuser', PASSWORD='ftppass');

-- Traditional
CREATE CONNECTION ftp_legacy ON FTP('ftp.example.com') WITH(USER='ftpuser', PASSWORD='ftppass');
```

---

### 4.3 Azure Blob Storage (`AZURE_BLOB`)
Aliases: `BLOB`

Cloud storage connector for reading and writing files in Azure Blob Storage containers.

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `CONTAINER` | Target blob container name | Yes |
| `ACCOUNT_NAME` | Azure storage account name | No |
| `ACCOUNT_KEY` | Azure storage account key | No |

> [!NOTE]
> You can provide a full SAS connection string in the traditional syntax, or use `ACCOUNT_NAME` + `ACCOUNT_KEY` in structured syntax.

*Examples:*
```sql
-- Full connection string (SAS or AccountKey)
CREATE CONNECTION cloud ON AZURE_BLOB('DefaultEndpointsProtocol=https;AccountName=myaccount;AccountKey=abc...')
    WITH(CONTAINER='backup-archive');

-- Structured with account credentials
CREATE CONNECTION cloud_struct ON AZURE_BLOB()
    WITH(ACCOUNT_NAME='myaccount', ACCOUNT_KEY='abc...', CONTAINER='raw-data');
```

---

### 4.4 REST API (`API`)
Aliases: `REST`, `HTTP`

Universal connector for web services and REST APIs returning JSON data.

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `URL` | The endpoint URL | Yes |
| `METHOD` | HTTP method: `GET`, `POST`, `PUT`, `DELETE` (Default: `GET`) | No |
| `AUTH_TYPE` | Authentication mode: `NONE`, `BASIC`, `BEARER`, `APIKEY` (Default: `NONE`) | No |
| `USER` | Username (for `BASIC` auth) | No |
| `PASSWORD` | Password (for `BASIC` auth) | No |
| `TOKEN` | Secret token (for `BEARER` or `APIKEY` auth) | No |
| `HEADER_NAME` | Header name for `APIKEY` auth (e.g. `X-API-Key`) | No |
| `ROOT_PATH` | JSONPath to the data array within the response (e.g. `$.items`) | No |
| `BODY` | JSON request body for `POST`/`PUT` requests | No |
| `PAG_TYPE` | Pagination style: `NONE`, `OFFSET` (Default: `NONE`) | No |
| `PAG_LIMIT` | Batch size / page size for paginated APIs | No |

*Examples:*
```sql
-- Public GitHub API — array is the root response
CREATE CONNECTION github_issues ON API()
    WITH(URL='https://api.github.com/repos/microsoft/terminal/issues', ROOT_PATH='$');

SELECT title, created_at FROM github_issues;

-- Bearer token authentication
CREATE CONNECTION my_api ON API()
    WITH(URL='https://api.example.com/v1/customers',
         AUTH_TYPE='BEARER',
         TOKEN='sk_live_abc123');

-- APIKEY header auth
CREATE CONNECTION weather ON API()
    WITH(URL='https://api.weather.com/data',
         AUTH_TYPE='APIKEY',
         TOKEN='my_api_key_value',
         HEADER_NAME='X-API-Key');

-- POST with a JSON body
CREATE CONNECTION submit ON API()
    WITH(URL='https://api.example.com/events',
         METHOD='POST',
         AUTH_TYPE='BEARER',
         TOKEN='tok_live_xyz',
         BODY='{"type":"etl_run","status":"complete"}');

-- Paginated API with OFFSET-style paging
CREATE CONNECTION pages ON API()
    WITH(URL='https://api.example.com/records',
         ROOT_PATH='$.data',
         PAG_TYPE='OFFSET',
         PAG_LIMIT=100);
```

---

### 4.5 Email (`SMTP`)
Aliases: `EMAIL`

Outbound-only email connector used with the `SEND EMAIL` statement.

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `PORT` | SMTP server port (Default: `25`) | No |
| `USERNAME` | Authentication username | No |
| `PASSWORD` | Authentication password | No |
| `USE_SSL` | Enable TLS/SSL (`TRUE`/`FALSE`, Default: `FALSE`) | No |
| `DEFAULT_FROM` | Default sender address when `FROM` is omitted in `SEND EMAIL` | No |

*Examples:*
```sql
-- Gmail with TLS
CREATE CONNECTION mailer ON SMTP('smtp.gmail.com')
    WITH(PORT=587, USERNAME='alerts@example.com', PASSWORD='apppassword',
         USE_SSL=TRUE, DEFAULT_FROM='alerts@example.com');

SEND EMAIL
    TO 'ops@example.com'
    SUBJECT 'Nightly Load Complete'
    BODY 'All records processed.'
    AT mailer;
```

---

### 4.6 Local Directory (`DIRECTORY`)
Treats a local filesystem folder as a data source for file management operations (`COPY FILE`, `DELETE FILE`, etc.) and directory listing via `SELECT`.

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `PATH` | Absolute directory path | Yes (structured) |
| `CREATE` | `ON`/`OFF` — create the directory if it doesn't exist (Default: `ON`) | No |

*Examples:*
```sql
CREATE CONNECTION data_dir ON DIRECTORY('C:\Data\Incoming') WITH(CREATE=ON);

-- List all files in the directory as a result set
SELECT * FROM data_dir;
```

#### Result Set Schema
When querying a `DIRECTORY` connection via `SELECT` the table name is `FILE` and the following columns are returned:
- `FileName` (STRING): Filename with extension.
- `Path` (STRING): Absolute path to the file.
- `Extension` (STRING): File extension (including dot).
- `Size` (DECIMAL): File size in bytes.
- `LastModified` (DATETIME): Last write time.
- `IsReadOnly` (BIT): `TRUE` if the file is read-only.
- `CreationTime` (DATETIME): Time the file was created.

---

## 5. Development & Testing: `MOCKDB`

ETL-SQL provides a built-in, zero-configuration in-memory database for script development and testing. No credentials, no server, no configuration required.

```sql
CREATE CONNECTION <name> ON MOCKDB();
```

### Pre-populated Tables

| Table | Columns |
| :--- | :--- |
| `Users` | `UserID`, `UserName`, `Email` |
| `Products` | `ProductID`, `ProductName`, `Price` |
| `Orders` | `OrderID`, `OrderDate`, `TotalAmount` |
| `Employee` | `ID`, `Name`, `Status`, `Active`, `first_name`, `last_name` |
| `departments` | `column1`, `column2`, `column3` |

All tables are pre-seeded with sample rows. `INSERT`, `UPDATE`, and `DELETE` operations are accepted but **do not persist** between sessions.

*Example:*
```sql
CREATE CONNECTION m ON MOCKDB();

SELECT u.UserName, o.TotalAmount
INTO #UserOrders
FROM m.Users AS u
JOIN m.Orders AS o ON u.UserID = o.OrderID;

-- Test an EXECUTE block
EXECUTE m INTO #emp
BEGIN
    SELECT ID, Name FROM Employee WHERE Active = 1;
END
```

> [!WARNING]
> `MOCKDB` is strictly for development and testing. Do not use it in production scripts.

---

## 6. Security Utilities

### 6.1 `USE PASSWORD`
Sets the master password for the current session used to decrypt `ENC:` connection strings.

```sql
USE PASSWORD = 'myMasterSecret';
CREATE CONNECTION db ON MSSQL('ENC:U2FsdGVkX1+...');
```

> [!NOTE]
> This is the **session master password**, not a connector credential. It is used only for `ENC:` string decryption.

### 6.2 `CREATE SSH_KEY_PAIR`
Generates an SSH key pair (public and private) at the specified directory. Supports SQL-style and function-style syntax.

*SQL Style (with named options):*
```sql
CREATE SSH_KEY_PAIR '<directory_path>'
    [WITH(BITS=2048, ALGORITHM='RSA', PASSPHRASE='pwd', COMMENT='comment')];
```

*Function Style (positional):*
```sql
SSH_KEY_PAIR('<directory_path>' [, bits, 'algorithm', 'passphrase', 'comment']);
```

| Option | Description | Default |
| :--- | :--- | :--- |
| `BITS` | Key size in bits (2048, 3072, 4096 for RSA; 256, 384, 521 for ECDSA) | `2048` |
| `ALGORITHM` | `RSA`, `ECDSA` | `RSA` |
| `PASSPHRASE` | Passphrase to encrypt the private key | *(none)* |
| `COMMENT` | Comment embedded in the public key file | *(none)* |

*Examples:*
```sql
-- Standard RSA key
CREATE SSH_KEY_PAIR 'C:\Keys\id_rsa';

-- 4096-bit RSA with passphrase
CREATE SSH_KEY_PAIR 'C:\Keys\id_rsa_prod'
    WITH(BITS=4096, PASSPHRASE='StrongPassword123!', COMMENT='Production ETL Service Account');

-- ECDSA key
SSH_KEY_PAIR('C:\Keys\id_ecdsa', 384, 'ECDSA', 's3cr3t');
```

---

## 7. Connection Lifecycle Commands

### 7.1 `DROP CONNECTION`
Closes and removes a connection from the current session. Frees connection pool slots and file handles.

```sql
DROP CONNECTION IF EXISTS legacy_db;
```

### 7.2 `ALTER CONNECTION`
Modifies properties of an existing connection. All unspecified properties are preserved.

```sql
ALTER CONNECTION remote_srv WITH(PASSWORD='new_rotated_password');
```

### 7.3 `CREATE OR ALTER CONNECTION`
Upserts a connection. If it exists, it is completely rebuilt with only the new options provided (previous options are NOT preserved).

```sql
CREATE OR ALTER CONNECTION remote_srv ON MSSQL('Server=db;Database=DW;')
    WITH(TABLE='dbo.Config');
```

### 7.4 `SHOW CONNECTIONS`
Lists all active connections in the current session.

```sql
SHOW CONNECTIONS [INTO #temp];
```

### 7.5 `HELP CONNECTION <type>`
Displays the connector's supported options and authentication patterns directly in the messages panel.

```sql
HELP CONNECTION MSSQL;
HELP CONNECTION SFTP;
HELP CONNECTION FLATFILE;
```

---

## 8. Quick Reference Table

| Token | Aliases | Type | Pushdown | Transactional |
| :--- | :--- | :--- | :---: | :---: |
| `MSSQL` | `SQL`, `SQLSERVER` | Relational | ✓ | ✓ |
| `POSTGRES` | `NPSQL`, `PG` | Relational | ✓ | ✓ |
| `ORACLE` | — | Relational | ✓ | ✓ |
| `ODBC` | — | Relational | Varies | — |
| `MOCKDB` | — | In-memory | — | — |
| `FLATFILE` | `CSV`, `TSV` | File | — | — |
| `EXCEL` | `XLSX`, `XLS` | File | — | — |
| `JSON` | — | File | — | — |
| `XML` | — | File | — | — |
| `PARQUET` | — | File | — | — |
| `AVRO` | — | File | — | — |
| `API` | `REST`, `HTTP` | Protocol | — | — |
| `SFTP` | `SSH` | Protocol | — | — |
| `FTP` | `FTP_CONN`, `FTPS` | Protocol | — | — |
| `AZURE_BLOB` | `BLOB` | Protocol | — | — |
| `SMTP` | `EMAIL` | Protocol | — | — |
| `DIRECTORY` | — | File | — | — |
