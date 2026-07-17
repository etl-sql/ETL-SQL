# SHAREPOINT
Connects to a SharePoint site to perform file operations against Document Libraries and read/write queries against Lists.

Syntax:
  CREATE CONNECTION <name> AS SHAREPOINT(
    URL               = 'https://company.sharepoint.com/sites/Finance',
    AUTH_MODE         = 'ENTRA_ID', -- ENTRA_ID, AD_WINDOWS, INTEGRATED, ADFS
    TENANT_ID         = '<tenant-id>',
    CLIENT_ID         = '<client-id>',
    CLIENT_SECRET     = '<client-secret>',
    DOCUMENT_LIBRARY  = 'Shared Documents',
    LIST_NAME         = '<list-name>'
  );

Options:
- **URL** — SharePoint Site absolute URL (can also be passed as primary connection string)
- **AUTH_MODE** — Authentication type: ENTRA_ID, AD_WINDOWS, INTEGRATED, ADFS (default: INTEGRATED)
- **USER** — Domain username (for AD_WINDOWS / ADFS)
- **PASSWORD** — Account password (for AD_WINDOWS / ADFS)
- **DOMAIN** — Domain context (for AD_WINDOWS / ADFS)
- **TENANT_ID** — Entra ID Directory/Tenant ID (for ENTRA_ID)
- **CLIENT_ID** — Entra ID Application Client ID (for ENTRA_ID)
- **CLIENT_SECRET** — Entra ID Application Client Secret (for ENTRA_ID)
- **DOCUMENT_LIBRARY** — SharePoint Document Library name (default: 'Shared Documents')
- **LIST_NAME** — SharePoint List to bind by default for data queries

```sql
-- Connect using Entra ID OAuth
CREATE CONNECTION SpFinance AS SHAREPOINT('https://company.sharepoint.com/sites/Finance',
  AUTH_MODE     = 'ENTRA_ID',
  TENANT_ID     = '00000000-0000-0000-0000-000000000000',
  CLIENT_ID     = '11111111-1111-1111-1111-111111111111',
  CLIENT_SECRET = ENC:U2FsdGVkX1+...
);

-- Copy local report to SharePoint
SEND FILE 'C:\reports\QuarterlySummary.xlsx' TO 'Shared Documents/Finance/Summary.xlsx' AT SpFinance;

-- Read items from SharePoint List
SELECT Title, Priority, PercentComplete
INTO #temp_tasks
FROM SpFinance
WITH(LIST_NAME='Tasks');
```

References:
- [Data Connectors](../../../administration/platform/README.md)
