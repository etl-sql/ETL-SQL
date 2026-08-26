# Connection Wizard Guide

The Connection Wizard provides interactive, schema-driven connection authoring across all ETL-SQL surfaces—including the VS Code Extension, Report Builder, Workstation Editor, and Portal Admin.

It preserves the **code-first** foundation of ETL-SQL: instead of locking database credentials and configurations into proprietary binary files or hidden GUI settings, the wizard emits standard, version-controllable `CREATE CONNECTION` script statements.

---

## 1. Operating Modes

The Connection Wizard operates in two primary modes depending on where it is invoked:

| Mode | Surface | Primary Output | Target Action |
| :--- | :--- | :--- | :--- |
| **Script Mode** | VS Code Extension, Report Designer, Workstation Editor | `CREATE CONNECTION` SQL statement | Inserts directly into the active script or report definition. |
| **Admin Mode** | Portal Admin (`Admin → Shared Connections`) | Governed Catalog Record | Submits to `/api/admin/connections` with environment scoping. |

---

## 2. Zero-Trust Security & Secret Handling

The Connection Wizard enforces Zero-Trust security rules at the authoring surface:

- **No Plaintext Passwords**: Passwords and connection secrets are never stored in raw text. The authentication switcher offers:
  - **Vault Secrets (`SECRET:key`)**: References keys stored in the platform secrets vault or environment.
  - **Environment Variables (`$ENV{name}`)**: Resolves variables from the host execution environment.
  - **Client Encryption (`ENC:...`)**: Client-side passphrase encryption before code emission.
  - **Windows / SSPI (`TRUSTED_CONNECTION = ON`)**: Zero-password Kerberos/Integrated security.
  - **Key Files (`KEY_FILE = '...'`)**: Secure private key authentication for SFTP and cloud storage.
- **Path Boundary Validation**: File connectors (`FLATFILE`, `PARQUET`, `EXCEL`) validate that all paths are workspace-relative (e.g. `data/sales.csv`). Absolute system roots (`C:\`, `/`), directory traversal (`..`), system directories (`/etc`, `Windows`), and script files (`.sql`, `.etlsql`, `.rptsql`) are blocked.

---

## 3. Connection String & URI Decomposition

Instead of manually mapping vendor connection parameters, click **Paste String** in the wizard header. The parser decomposes standard connection strings:

```text
Server=sql01.corp.internal,1433;Database=SalesDW;User Id=app_reader;Password=Secret123;TrustServerCertificate=true;
```

The wizard parses the string into canonical ETL-SQL options:
- Normalizes vendor keys (`Data Source` → `SERVER`, `Initial Catalog` → `DATABASE`, `UID` → `USER`).
- Automatically isolates passwords into suggested vault keys (e.g. `MSSQL_SALESDW_PW`).
- Previews the resulting ETL-SQL statement immediately.

---

## 4. Layered Diagnostic Probing

Before saving or inserting a connection, click **Test Connection** to execute a 4-layer diagnostic reachability probe:

```
[POLICY]  ✓ Destination permitted by tenant network policy
[DNS]     ✓ 'sql01.corp.internal' resolved to 10.20.4.15
[TCP]     ✓ Established TCP socket handshake on port 1433 (12ms)
[AUTH]    ✓ Validated credential handshake against remote engine
```

If any check fails, the wizard displays the failing layer along with specific remediation guidance (such as firewall port access or certificate trust options).

---

## 5. Hybrid Connectivity & Data Gateways

For on-premises data sources located behind enterprise firewalls, select an enrolled **Data Gateway** cluster from the Gateway Routing dropdown.

The wizard emits the `GATEWAY` routing option:

```sql
CREATE CONNECTION onprem_erp AS MSSQL(
  SERVER = 'erp.local.corp',
  DATABASE = 'EnterpriseDB',
  USER = 'svc_etl',
  PASSWORD = SECRET:ERP_READER_PW,
  GATEWAY = 'dallas-cluster-01'
);
```

---

## 6. Authoring Examples

### Standard SQL Server Connection

```sql
CREATE CONNECTION sales_dw AS MSSQL(
  SERVER = 'sql01.corp.internal',
  PORT = 1433,
  DATABASE = 'SalesDW',
  USER = 'app_reader',
  PASSWORD = SECRET:SQL_READER_PW,
  ENCRYPT = TRUE,
  TRUST_SERVER_CERTIFICATE = TRUE,
  TIMEOUT_SECONDS = 30
);
```

### Delimited CSV Flat File Connection

```sql
CREATE CONNECTION staging_orders AS FLATFILE(
  PATH = 'data/orders_2026.csv',
  DELIMITER = ',',
  HEADER = TRUE
);
```

### Shared Catalog Reference Connection

```sql
CREATE CONNECTION dw AS MSSQL('SHARED:corp_sales_dw');
```

---

## References

- [Statement Reference: CREATE CONNECTION](../../reference/statements/ddl/create.md)
- [Data Connectors Hub](../../reference/connectors/README.md)
- [Secure Outbound Gateway](../../administration/platform/secure-outbound-gateway.md)
