---
trigger: $sharepoint
label: CREATE CONNECTION … ON SHAREPOINT
description: SharePoint Document Library or List connection via Entra ID or domain login
---
CREATE CONNECTION «ConnName» AS SHAREPOINT('«https://company.sharepoint.com/sites/Finance»',
  AUTH_MODE     = 'ENTRA_ID',
  TENANT_ID     = '«tenant-id»',
  CLIENT_ID     = '«client-id»',
  CLIENT_SECRET = '«client-secret»'
);
