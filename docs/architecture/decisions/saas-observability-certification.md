# SaaS Observability and Support Access Certification

## Overview
This document serves as the adversarial certification evidence for **SaaS Domain 8: Audit, Observability, and Support Access**. It formally attests that tenant telemetry boundaries and support-access isolation remain intact under both Managed Dedicated and Shared SaaS deployment profiles.

## Certification Vectors

### 1. Tenant Telemetry Separation
* **Vector**: Cross-tenant data leakage via aggregated logs, metrics, or diagnostic health checks.
* **Evidence (Pass)**:
  * **Managed Dedicated**: Telemetry is routed to a tenant-specific data store physically isolated at the infrastructure level. Application logs do not cross network boundaries to a shared observability sink without tenant-approved configurations.
  * **Shared SaaS**: All observability events, audit trails, and job histories derive their scoping label (`TenantId`) strictly from the server-validated execution context (`StorageCapability` / `ExecutionIdentity`), not from client-provided properties. Cross-tenant aggregation strictly filters on these server-derived labels, rendering cross-tenant telemetry bleed impossible at the querying layer.

### 2. Support-Access Separation (Zero-Trust Platform Authority)
* **Vector**: Platform operators using root access to implicitly read tenant SQL, bypass audit logs, or impersonate tenants via shared support tooling.
* **Evidence (Pass)**:
  * **Persisted Statement Text is Tenant Data**: In ETL-SQL, the `.etlsql` and `.rptsql` source code, along with in-flight `operationId` logs, is considered sensitive tenant data. It may contain proprietary logic or PII.
  * **Controlled Triage**: Platform triage operates via a **controlled support access** model rather than implicit platform authority. 
  * Operations support tooling requires time-limited, purpose-bound support capability tokens approved under the tenant's execution policy. Raw database access or standing impersonation tokens are strictly prohibited.
  * **Redaction**: All secret references (`SECRET:name`), connection strings, and capability tokens are redacted before reaching the durable remote audit outbox. 

### 3. Aggregate Platform Health
* **Vector**: Tenant script content or data shape leaked through macro platform health metrics (e.g., "Top failing jobs").
* **Evidence (Pass)**:
  * Health probes (`/healthz`) and aggregated platform telemetry metrics strictly report at the engine execution layer (e.g., node capacity, heartbeat TTls, available execution slots).
  * No tenant script content, variables, or error payload strings are included in aggregate health dimensional reporting.

---
**Status**: Certified
**Date**: August 2026
