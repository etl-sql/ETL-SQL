# ETL-SQL Data Connectors: Reference & Guide

Connectors define how the ETL-SQL engine interacts with external data sources, such as databases, files, and remote systems. This document provides instructional examples and property references for every supported connector.

---

## 1. Syntax Overview

Before working with any connector, you must understand the two ways to define connection statements in ETL-SQL:

### 1.1 Property Mode (Structured Syntax)
Recommended for readability and AI agent configuration. All parameters are passed explicitly as properties inside the `WITH()` clause.

```sql
CREATE CONNECTION my_db ON MSSQL() WITH(SERVER='prod-db', DATABASE='ERP', USER='admin', PASS='secret');
```

### 1.2 Traditional Mode (String-based Syntax)
Standard approach. The connection string is provided as the primary argument. This is especially useful for native driver strings or encrypted (`ENC:...`) values.

```sql
CREATE CONNECTION legacy_db ON MSSQL('Server=prod-db;Database=ERP;User Id=admin;Password=secret;');
```

> [!TIP]
> Both formats achieve the exact same result. Use the one that is easiest to read or matches your source control policies.

---

## 2. Relational Database Connectors

Database connections support SQL pushdown, meaning ETL-SQL will execute operations natively on the server whenever possible.

### 2.1 Oracle (`ORACLE`)
Oracle supports two distinct connection patterns.

| Property | Description | Mandatory |
| :--- | :--- | :---: |
| `HOST` | Server name or IP | Yes (Service Pattern) |
| `PORT` | Listening port (Default: 1521) | No |
| `SERVICE_NAME` | The Oracle Service Name | Yes (Service Pattern) |
| `TNS_NAME` | The Oracle TNS alias | Yes (TNS Pattern) |
| `USER` | Login username | Yes |
| `PASSWORD` | Login password | Yes |
| `POOLING` | Enable connection pooling (`TRUE`/`FALSE`) | No |
| `MIN_POOL_SIZE` | Minimum number of connections in the pool | No |
| `MAX_POOL_SIZE` | Maximum number of connections in the pool | No |
| `CONNECTION_LIFETIME` | Maximum time (sec) a connection stays alive | No |

> [!CAUTION]
> **Mutual Exclusivity**: You cannot use `TNS_NAME` and `SERVICE_NAME` in the same connection block.

*Examples:*
```sql
-- Service Pattern (Structured)
CREATE CONNECTION o_dev ON ORACLE() 
    WITH(HOST='oradb.local', PORT=1521, SERVICE_NAME='ORCL', USER='app_user', PASSWORD='pwd');

-- TNS Pattern (Traditional)
CREATE CONNECTION o_prod ON ORACLE('Data Source=MyTNS;User Id=app_user;Password=pwd;');
```

### 2.2 Microsoft SQL Server (`MSSQL`)
Supports standard authentication and Windows Integrated Security.

| Property | Description | Mandatory |
| :--- | :--- | :---: |
| `SERVER` | Server name or IP | Yes |
| `DATABASE` | Target database name | Yes |
| `USER` | SQL username | No (if Trusted) |
| `PASSWORD` | SQL password | No (if Trusted) |
| `TRUSTED_CONNECTION` | Use Windows Auth (`TRUE`/`FALSE`) | No |
| `APPLICATION_INTENT`| `READWRITE` or `READONLY` | No |
| `MULTI_SUBNET_FAILOVER`| Optimized failover for multi-subnet clusters | No |
| `MIN_POOL_SIZE` | Minimum connections in pool | No |
| `MAX_POOL_SIZE` | Maximum connections in pool | No |
| `POOL_LIFETIME` | Time (sec) for load balancing timeout | No |
| `CONNECT_TIMEOUT` | Time (sec) to wait for a connection | No |
| `TABLE` | Set a default table context | No |

*Examples:*
```sql
-- Standard Authentication (Structured)
CREATE CONNECTION m_sales ON MSSQL() 
    WITH(SERVER='sql01', DATABASE='SalesDB', USER='etl_worker', PASSWORD='pwd');

-- Windows Authentication (Traditional)
CREATE CONNECTION m_hr ON MSSQL('Server=sql01;Database=HR;Trusted_Connection=True;');
```

### 2.3 PostgreSQL (`POSTGRES`)
| Property | Description | Mandatory |
| :--- | :--- | :---: |
| `HOST` | Server name or IP | Yes |
| `DATABASE` | Target database name | Yes |
| `USER` | Login username | Yes |
| `PASSWORD` | Login password | Yes |
| `PORT` | Listening port (Default: 5432) | No |
| `POOLING` | Enable pooling (`TRUE`/`FALSE`) | No |
| `MIN_POOL_SIZE` | Minimum pool size | No |
| `MAX_POOL_SIZE` | Maximum pool size | No |
| `CONNECTION_IDLE_LIFETIME` | Idle timeout for connections | No |
| `SSL_MODE` | `DISABLE`, `REQUIRE`, `VERIFYFULL`, etc. | No |
| `TRUST_SERVER_CERTIFICATE` | Bypass certificate validation | No |

*Examples:*
```sql
-- Structured Syntax
CREATE CONNECTION pg_db ON POSTGRES() 
    WITH(HOST='10.0.0.5', PORT=5432, DATABASE='inventory', USER='admin', PASSWORD='pwd');
```

### 2.4 ODBC Bridge (`ODBC`)
Universal bridge for any source with a local ODBC driver. Supports DSN-based and DSN-less connections.

| Property | Description | Mandatory |
| :--- | :--- | :---: |
| `DSN` | Pre-configured Data Source Name | No |
| `DRIVER` | Name of driver in `{}` | No |
| `SERVER` | Server name or IP | No |
| `PORT` | Listening port | No |
| `DATABASE` | Database name or file path | No |
| `UID` | Login username | No |
| `PWD` | Login password | No |
| `CONNECT_TIMEOUT` | Login timeout (sec) | No |

> [!NOTE]
> For **DSN-less** connections, you must provide the `DRIVER` and at least one identifying property like `SERVER` or `DATABASE`.

*Examples:*
```sql
-- DSN Pattern
CREATE CONNECTION my_dsn ON ODBC() WITH(DSN='ProdSales', UID='etl', PWD='pwd');

-- DSN-less Pattern (e.g. SQLite)
CREATE CONNECTION my_sqlite ON ODBC() 
    WITH(DRIVER='{SQLite3 ODBC Driver}', DATABASE='C:\Data\local.db');
```

### 2.5 REST API Connector (`API`)
Universal connector for web services and cloud APIs returning JSON data.

| Property | Description | Mandatory |
| :--- | :--- | :---: |
| `URL` | The endpoint URL | Yes |
| `METHOD` | HTTP Method (GET, POST, etc.) | No |
| `AUTH_TYPE` | NONE, BASIC, BEARER, APIKEY | No |
| `USER` | Username (for BASIC) | No |
| `PASSWORD` | Password (for BASIC) | No |
| `TOKEN` | Secret (for BEARER/APIKEY) | No |
| `HEADER_NAME`| Header for APIKEY (e.g. X-API-Key) | No |
| `ROOT_PATH` | JSONPath to the data array | No |
| `BODY` | Request body for POST/PUT | No |

*Examples:*
```sql
-- Fetch GitHub Issues (Public)
CREATE CONNECTION github ON API() 
    WITH(URL='https://api.github.com/repos/microsoft/terminal/issues', ROOT_PATH='$');

-- Authenticated Private API
CREATE CONNECTION my_service ON API() 
    WITH(URL='https://api.internal.com/v1/customers', 
         AUTH_TYPE='BEARER', 
         TOKEN='sk_live_12345');
```

---

## 3. Flat File & Document Connectors

### 3.1 Flat Files (`FLATFILE` / `CSV`)
Used for reading delimited or fixed-width text files.

| Property | Description | Mandatory |
| :--- | :--- | :---: |
| `PATH` | Absolute path to the file | Yes |
| `DELIMITER` | `COMMA`, `PIPE`, `TAB`, or a literal `<char>` | No |
| `HEADER` | `ON`, `OFF` (Default: ON) | No |
| `FORMAT` | `DELIMITED` (Default) or `FIXED` | No |
| `TEMPLATE` | Table name defining fixed widths (e.g., `#tmp`) | Yes (if FIXED) |
| `COMPRESS` | Transparent GZip support `ON`/`OFF` | No |
| `ENCRYPT` | AES encryption `ON`/`OFF` | No |
| `ENCODING` | `UTF8`, `ANSI`, `UTF16`, `LATIN1`, etc. | No |
| `CULTURE` | Locale for parsing (e.g., `en-US`, `de-DE`) | No |
| `TRIM` | `ON`/`OFF` (Manage whitespace) | No |

*Examples:*
```sql
-- Delimited File
CREATE CONNECTION csv_in ON FLATFILE() 
    WITH(PATH='C:\Data\employees.csv', HEADER=ON, DELIMITER=COMMA);

-- Encrypted and Compressed
CREATE CONNECTION secure_file ON FLATFILE('C:\Data\payroll.csv.gz') 
    WITH(COMPRESS=ON, ENCRYPT=ON, PASSWORD='s3cr3t');
```

#### Working with Fixed-Width Files
To read a fixed-width file, you must first define a "Template" table so the engine knows how to slice the text. Use the `/* @width: N */` metadata tag to set boundaries.

```sql
-- 1. Define the width layout
CREATE TABLE #EmployeeLayout (
    ID INT /* @width: 5 */,
    Name VARCHAR(20) /* @width: 20 */,
    DeptCode CHAR(3) /* @width: 3 */
);

-- 2. Pass the template to the connection
CREATE CONNECTION fixed_emp ON FLATFILE('employees.dat')
    WITH(FORMAT='FIXED', TEMPLATE=#EmployeeLayout, HEADER=OFF);
```

### 3.2 Excel (`EXCEL`)
Reads data from `.xlsx`, `.xls`, or `.xlsb` files.

| Property | Description | Mandatory |
| :--- | :--- | :---: |
| `PATH` | Absolute path to the file | Yes |
| `SHEET` | Target sheet name (Def: first sheet) | No |
| `HEADER` | `ON`/`OFF` (Def: ON) | No |
| `RANGE` | Explicit cell range (e.g., 'A1:D100') | No |

*Examples:*
```sql
CREATE CONNECTION xl_src ON EXCEL('C:\Reports\Q4.xlsx') 
    WITH(SHEET='Summary', HEADER=ON, RANGE='A1:F500');
```

### 3.3 Parquet & Avro (`PARQUET` / `AVRO`)
Big Data columnar formats.

| Property | Description | Mandatory |
| :--- | :--- | :---: |
| `PATH` | Absolute path to the file | Yes |
| `COMPRESSION`| `SNAPPY` (Def), `GZIP`, `UNCOMPRESSED` | No |

*Examples:*
```sql
CREATE CONNECTION pq_out ON PARQUET() WITH(PATH='C:\Data\output.parquet', COMPRESSION=SNAPPY);
```

### 3.4 JSON & XML (`JSON` / `XML`)
Document extraction with deep-nesting support.

| Property | Description | Mandatory |
| :--- | :--- | :---: |
| `PATH` | Absolute path to the file | Yes |
| `ROOT_PATH` | JSONPath (`$.Array`) or XPath (`/Root/Node`) | No |
| `ENCODING` | character encoding for reading | No |
| `TRIM` | `ON`/`OFF` (Manage whitespace) | No |

*Examples:*
```sql
-- Drill into a specific nested array
CREATE CONNECTION json_src ON JSON('C:\Data\orders.json') WITH(ROOT_PATH='$.data.orders');
```

---

## 4. Remote & Cloud Protocol Connectors

### 4.1 SFTP & SSH (`SFTP`)
Secure File Transfer Protocol.

| Property | Description | Mandatory |
| :--- | :--- | :---: |
| `HOST` | Server domain or IP | Yes |
| `PORT` | Listening port (Default: 22) | No |
| `USER` | Login username | Yes |
| `PASSWORD` | Login password (if using basic auth) | No |
| `KEYFILE` | Path to private SSH key | No |
| `PASSPHRASE` | Passphrase for the keyfile | No |

*Examples:*
```sql
-- Password Authentication
CREATE CONNECTION sftp_pwd ON SFTP() 
    WITH(HOST='sftp.example.com', USER='admin', PASSWORD='secret');

-- Keyfile Authentication
CREATE CONNECTION sftp_key ON SFTP('sftp.example.com') 
    WITH(USER='deploy', KEYFILE='/home/etl/.ssh/id_rsa', PASSPHRASE='keypass');
```

### 4.2 FTP (`FTP`)
Legacy File Transfer Protocol.
```sql
CREATE CONNECTION ftp_conn ON FTP('ftp.example.com') WITH(USER='ftpuser', PASSWORD='ftppass');
```

### 4.3 Azure Blob Storage (`AZURE_BLOB`)
Cloud storage extraction.
```sql
CREATE CONNECTION cloud_store ON AZURE_BLOB('DefaultEndpointsProtocol=https;AccountName=myacc;AccountKey=abc...')
    WITH(CONTAINER='backup-archive');
```

### 4.4 Local Directory (`DIRECTORY`)
Treats a local filesystem directory as a data source for file manipulation tasks (`COPY_FILE`, `DELETE_FILE`, etc.).
```sql
CREATE CONNECTION data_dir ON DIRECTORY('C:\Data\Incoming') WITH(CREATE=ON);
```

### 4.5 Email (`SMTP`)
Outbound email automation.
```sql
CREATE CONNECTION mailer ON SMTP('smtp.gmail.com')
    WITH(PORT=587, USERNAME='alerts@example.com', PASSWORD='apppassword', USE_SSL=TRUE);
```

---

## 5. Development & Testing: `MOCKDB`

ETL-SQL provides a built-in, zero-configuration in-memory database designed exclusively for script development and testing. It requires no credentials.

`CREATE CONNECTION <name> ON MOCKDB();`

### Pre-populated Tables
When you instantiate `MOCKDB`, the engine generates these tables with sample rows so you can immediately practice writing queries, performing joins, expanding active tables, and testing flow logic:

- `Users`: (UserID, UserName, Email)
- `Products`: (ProductID, ProductName, Price)
- `Orders`: (OrderID, OrderDate, TotalAmount)
- `Employee`: (ID, Name, Status, Active, first_name, last_name)

*Example:*
```sql
CREATE CONNECTION m ON MOCKDB();

SELECT u.*, o.TotalAmount 
INTO #UserOrders 
FROM m.Users AS u 
JOIN m.Orders AS o ON u.UserID = o.OrderID;
```

> [!WARNING]
> `MOCKDB` is strictly an ephemeral workspace. Data inserted or modified in `MOCKDB` does not persist after the script terminates.

---

## 6. Connection Lifecycle Commands

Managing the scope and duration of connections helps prevent resource leaks.

### 6.1 `DROP CONNECTION`
Explicitly closes and destroys a connection from the current context.
```sql
DROP CONNECTION IF EXISTS legacy_db;
```

### 6.2 `ALTER CONNECTION`
Modifies an existing connection while preserving all other undisturbed properties.
```sql
ALTER CONNECTION remote_srv WITH(PASSWORD='new_rotated_password');
```

### 6.3 `CREATE OR ALTER CONNECTION`
Upserts a connection. If it exists, it is torn down and fully rebuilt with only the options provided.
```sql
CREATE OR ALTER CONNECTION remote_srv ON MSSQL('Server=db;...') WITH(TABLE='dbo.Config');
```
