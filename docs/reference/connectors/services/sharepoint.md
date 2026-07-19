# SHAREPOINT

Manages files in SharePoint Document Libraries (remote file-system operations) and reads/writes
SharePoint Lists.

Aliases: `SP`

## Options

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `AUTH_MODE` | Authentication mode: `INTEGRATED`, `AD_WINDOWS`, `ENTRA_ID`, `ADFS` (default: `INTEGRATED`) | No |
| `USER` | Domain account username or service account (for `AD_WINDOWS` and `ADFS`) | No |
| `PASSWORD` | Password (for `AD_WINDOWS` and `ADFS`) | No |
| `DOMAIN` | Domain name (for `AD_WINDOWS` and `ADFS`) | No |
| `CLIENT_ID` | Microsoft Entra ID application client ID (for `ENTRA_ID`) | No |
| `CLIENT_SECRET` | Microsoft Entra ID application client secret (for `ENTRA_ID`) | No |
| `TENANT_ID` | Microsoft Entra ID tenant/directory ID (for `ENTRA_ID`) | No |
| `DOCUMENT_LIBRARY` | Target Document Library path/title (default: `Shared Documents`) | No |
| `LIST_NAME` | Default list title for list queries | No |

> [!IMPORTANT]
> - `CLIENT_SECRET` and `PASSWORD` should always be encrypted using `ENC:` string values.
> - Plaintext secrets in `CLIENT_SECRET` or `CLIENTSECRET` trigger a linter warning.
> - When using `AUTH_MODE = 'ENTRA_ID'`, the options `TENANT_ID`, `CLIENT_ID`, and `CLIENT_SECRET` are
>   mutually required.

## Examples

```sql
-- Client credentials (Entra ID — recommended for cloud)
CREATE CONNECTION sp_cloud AS SHAREPOINT('https://tenant.sharepoint.com/sites/Finance',
         AUTH_MODE     = 'ENTRA_ID',
         TENANT_ID     = '00000000-0000-0000-0000-000000000000',
         CLIENT_ID     = '11111111-1111-1111-1111-111111111111',
         CLIENT_SECRET = ENC:U2FsdGVkX1+...);

-- Domain credentials (on-premises / AD_WINDOWS)
CREATE CONNECTION sp_onprem AS SHAREPOINT('https://sharepoint.local/sites/HR',
         AUTH_MODE = 'AD_WINDOWS',
         USER      = 'sp_service',
         PASSWORD  = ENC:U2FsdGVkX1+...,
         DOMAIN    = 'CORP');

-- Integrated authentication
CREATE CONNECTION sp_integrated AS SHAREPOINT('https://tenant.sharepoint.com/sites/IT', AUTH_MODE = 'INTEGRATED');
```

## References

- [Service Connectors](README.md)
- [Connectors](../README.md)
- [Active Directory](active-directory.md)
