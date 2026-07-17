# Portal User Permission Matrix

This matrix is the expected result source for `UserPermissionIntegrationTests` and manual login verification.

| User | Role | Groups | Finance | Finance/Invoices | Operations | Operations/Logs |
| --- | --- | --- | --- | --- | --- | --- |
| `admin_user` | Admin | none | Admin override | Admin override | Admin override | Admin override |
| `finance_pub` | Publisher | Finance Publishers | Execute | Manage | None | None |
| `finance_read` | Viewer | Finance Readers | Read | Read | None | None |
| `ops_read` | Viewer | Operations Readers | None | None | Read | Read |
| `manager_user` | Viewer | Managers | Execute | Read | Read | Execute |
| `outsider_user` | Viewer | Outsider Group | None | None | None | None |
| `no_group_user` | Viewer | none | None | None | None | None |

| Workflow | Admin | Finance Publisher | Finance Reader | Operations Reader | Manager | Outsider |
| --- | --- | --- | --- | --- | --- | --- |
| List/view Finance reports | Allow | Allow | Allow | Deny | Allow | Deny |
| Execute/refresh Finance report | Allow | Allow | Deny | Deny | Allow | Deny |
| Manage Invoice report metadata | Allow | Allow | Deny | Deny | Deny | Deny |
| Publish into Invoices | Allow | Allow | Deny | Deny | Deny | Deny |
| Create root folder | Allow | Deny | Deny | Deny | Deny | Deny |
| Favorite or create saved view for Finance report | Allow | Allow | Allow | Deny | Allow | Deny |
| Create Finance report alert | Allow | Allow | Deny | Deny | Allow | Deny |
| Create Finance report subscription | Allow | Allow | Allow | Deny | Allow | Deny |
| Discover Operations report through search/direct ID/export | Allow | Deny | Deny | Allow | Allow | Deny |
| View Finance private dataset | Allow | Editor | Viewer | Deny | Deny | Deny |
| View Operations private dataset | Allow | Deny | Deny | Viewer | Editor | Deny |
| Access admin users/groups/SMTP/metrics/audit/effective permissions | Allow | Deny | Deny | Deny | Deny | Deny |
| Access Orchestrator proxy/status | Allow | Deny | Deny | Deny | Deny | Deny |

Additional identity expectations:

- `inactive_user` cannot log in.
- `mcp_user` can log in but is blocked from regular APIs until changing the password.
- `revoked_user` cannot refresh a session after token revocation.
- LDAP-backed scenarios remain outside this local-only matrix until an LDAP integration fixture is available.

Sensitive operations should produce audit actions including `CREATE_USER`, `DELETE_USER`, `REVOKE_TOKENS`,
`ADD_USER_TO_GROUP`, `REMOVE_USER_FROM_GROUP`, `GRANT_PERMISSION`, `REVOKE_PERMISSION`, `PUBLISH_REPORT`,
`DELETE_REPORT`, `CREATE_SUBSCRIPTION`, `UPDATE_SUBSCRIPTION`, and `DELETE_SUBSCRIPTION`.
