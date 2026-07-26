# Self-Service Access Request Workflow

The Portal provides an interactive self-service access request workflow when users encounter restricted reports or folders. Instead of displaying a cold `403 Forbidden` error screen, the Portal presents a structured access request card containing safe owner metadata and 1-click request submission.

## Workflow Overview

```
┌─────────────────────────┐       1. Access Restricted Report      ┌─────────────────────────┐
│     Business Consumer    ├───────────────────────────────────────►│  Portal Access Control  │
└───────────┬─────────────┘                                        └────────────┬────────────┘
            │                                                                   │ 2. Returns 403 +
            │ 3. Clicks "Request Access"                                        │    ReportAccessInfoDto
            ▼                                                                   ▼
┌─────────────────────────┐       4. Writes Audit Event            ┌─────────────────────────┐
│  Request Access Form    ├───────────────────────────────────────►│  Audit Outbox & Log     │
└─────────────────────────┘                                        └────────────┬────────────┘
                                                                                │ 5. Notifies Owner
                                                                                ▼
                                                                   ┌─────────────────────────┐
                                                                   │  Report Owner / Admin   │
                                                                   └─────────────────────────┘
```

## API Endpoints

### 1. Fetch Access Metadata
`GET /api/reports/{id}/access-info`

Returns safe metadata for rendering the access request UI:
```json
{
  "reportId": 42,
  "reportName": "Q3 Regional Sales Summary",
  "folderPath": "/Finance/Sales",
  "owner": "Finance Analytics Team",
  "contact": "finance-lead@company.com",
  "description": "Quarterly regional sales breakdown.",
  "canRequestAccess": true
}
```

### 2. Submit Access Request
`POST /api/reports/{id}/request-access`

**Request Body:**
```json
{
  "reason": "Required for Q3 internal audit reconciliation."
}
```

**Response:**
```json
{
  "message": "Access request for 'Q3 Regional Sales Summary' submitted to report owner (Finance Analytics Team).",
  "reportId": 42,
  "reportName": "Q3 Regional Sales Summary",
  "owner": "Finance Analytics Team",
  "status": "PENDING_APPROVAL"
}
```

## Audit & Governance Logging
Every access request stages an immutable audit entry:
- **Action**: `REQUEST_REPORT_ACCESS`
- **Entity**: `Report` (`Id`)
- **Detail**: `Access requested for 'ReportName' by UserName. Reason: ...`

The request is queued in `AuditOutboxMessages` for email notification to the report owner or admin group.

## References
- [Portal Permissions](permissions.md)
- [Monitoring & Audit](monitoring-and-audit.md)
- [Administration Guide](../README.md)
