# Central Security Events and SIEM Delivery

Security events are separate from diagnostic logs and governance audit records: a dedicated versioned contract with a durable local outbox and optional SIEM delivery.

## By deployment profile

| Profile | What you do |
| :--- | :--- |
| **Solo / Workstation** | Events are written to the local outbox and stay there. Useful as evidence after the fact; there is nothing to deliver them to. |
| **Team / SME** | Optionally point the outbox at a collector. Delivery failure is visible but does not block work. |
| **Enterprise / Corporate** | Deliver to the SIEM, and decide deliberately whether mutations should **fail closed** when the collector is unhealthy. `GET /api/admin/audit/collector` reports what would happen to the next mutation rather than leaving you to infer it. |
| **SaaS / Departmental** | As Enterprise, **with a distinct outbox path per environment**. This is the one isolated resource whose default is actively wrong: `LocalApplicationData` is machine-wide, so two environments on one host write security events into a single queue — a cross-environment leak of exactly the records isolation exists to keep apart. Set it explicitly per environment. |

They carry the
same correlation, job, script, policy, tenant, and node identifiers when those values are available, but use a
dedicated versioned contract and durable local outbox. A policy denial is decided and enforced before reporting;
an unavailable event sink cannot turn a denial into an allow. Remote delivery affects execution only when an
enrolled organization's signed policy explicitly configures a fail-closed threshold.

Configure delivery in the signed organization policy. The collector endpoint must use HTTPS, must not contain
embedded credentials, and requires the enrolled machine's client certificate:

```json
{
  "schemaVersion": "1.0",
  "securityEvents": {
    "collectorEndpoint": "https://siem.example.com/etl-sql/security-events",
    "batchSize": 100,
    "intervalSeconds": 30,
    "leaseSeconds": 120,
    "minimumForwardedSeverity": "warning",
    "failClosedMaxTerminalFailures": 5,
    "failClosedMaxOldestEventSeconds": 900,
    "failClosedMaxPendingEvents": 1000,
    "failClosedMaxOutboxBytes": 104857600
  }
}
```

Omit all `failClosed*` values for local durability with best-effort remote forwarding. Standalone installations
write only to their local outbox and make no enterprise network calls. A severity filter removes lower-severity
rows from forwarding without changing local enforcement. Because the filter is authoritative policy, changing it
requires publishing a newly signed policy.

Standalone hosts use the OS local-application-data directory by default. Containers and certification harnesses
may set `ETLSQL_SECURITY_EVENT_OUTBOX_PATH` to an absolute path on a persistent writable volume. This override is
ignored for enrolled machines; their outbox remains beside the protected enrollment state.

> [!IMPORTANT]
> **The default path is machine-wide**, shared by every ETL-SQL process on the host. That is fine for a single
> deployment and wrong for two: co-located Portal and Orchestrator processes contend for one SQLite file, and two
> departmental environments on one machine write their security events into a single queue — a cross-environment
> leak of exactly the records isolation exists to keep apart. Set
> `ETLSQL_SECURITY_EVENT_OUTBOX_PATH` per environment whenever more than one deployment shares a host; the
> departmental environment plan (`GET /api/admin/environments/plan`) now lists it as a required isolated resource.
>
> The contention is observable: it was found because two test processes starting back to back could not both open
> the file, and the second failed to start at all.

Each request contains enrollment headers, an `Idempotency-Key` for the batch, schema header
`X-ETL-SQL-Security-Event-Schema: 1`, and this JSON envelope:

```json
{
  "schemaVersion": 1,
  "batchId": "<sha256-of-sorted-event-ids>",
  "events": [
    {
      "schemaVersion": 1,
      "eventId": "4f7578f2-46d4-40cb-8cba-cc08d186f409",
      "severity": "error",
      "type": "operationDenied",
      "timestampUtc": "2026-07-13T18:30:00Z",
      "actorIdentity": "user:42",
      "effectiveIdentity": "service:runner",
      "hostName": "etl-node-03",
      "nodeId": "machine-id",
      "tenantId": "production",
      "scriptHash": "<sha256>",
      "jobId": "job-9",
      "correlationId": "corr-17",
      "policyVersion": "v4",
      "policyHash": "<sha256>",
      "sanitizedTarget": "https://api.example.com",
      "decision": "denied",
      "reason": "Destination is outside the approved host policy."
    }
  ]
}
```

The collector must authenticate the client certificate and enrollment headers, deduplicate on `eventId`, and
return only IDs it durably accepted:

```json
{
  "acknowledgedEventIds": [
    "4f7578f2-46d4-40cb-8cba-cc08d186f409"
  ]
}
```

A 2xx response without an explicit acknowledgement does not remove an event. Unacknowledged rows are retried;
collectors must therefore make `eventId` unique in their ingestion store. The ETL-SQL schema remains the source
record. Apply vendor normalization in the collector or ingestion pipeline so changing SIEM products never changes
the engine contract.

| ETL-SQL field | Splunk CIM example | Elastic ECS example | Microsoft Sentinel ASIM example |
| :--- | :--- | :--- | :--- |
| `timestampUtc` | `_time` | `@timestamp` | `TimeGenerated`, `EventStartTime`, `EventEndTime` |
| `eventId` | `event_id` | `event.id` | `EventOriginalUid` |
| `type` | `signature`, `eventtype` | `event.action` | `EventOriginalType` |
| `severity` | `severity` | `event.severity` using a documented local numeric map | `EventOriginalSeverity`; normalize to `EventSeverity` |
| `decision` | `action` (`blocked`, `allowed`, `failed`) | `event.type` (`denied`, `allowed`, `error`) and `event.outcome` | `EventResult` and `EventResultDetails` |
| `reason` | `message` | `event.reason` | `EventMessage` |
| `actorIdentity` | `user` | `user.name` | `ActorUsername` |
| `effectiveIdentity` | custom `effective_user` | `user.effective.name` | `TargetUsername` or `AdditionalFields` |
| `hostName`, `nodeId` | `host`, custom `device_id` | `host.name`, `host.id` | `DvcHostname`, `DvcId` |
| `tenantId` | custom `tenant_id` | `organization.id` | `ActorScopeId` or `AdditionalFields` |
| `sanitizedTarget` | `dest` or `object` | `resource.name` or a domain-specific destination field | schema-specific target field or `AdditionalFields` |
| `correlationId`, `jobId`, `scriptHash`, `policyVersion`, `policyHash` | retain as custom fields | retain under `labels` or namespaced custom fields | retain in `AdditionalFields` |

For Elastic, a successfully enforced denial should normally use `event.type: denied` and
`event.outcome: success`; the policy action succeeded even though the requested operation did not. Use
`event.outcome: failure` for ETL-SQL `decision: failed`. For Sentinel, choose the specialized ASIM schema when
the target has clear file, network, process, or audit semantics; otherwise retain the source record and normalize
the common fields. Mapping references: [Splunk CIM fields](https://help.splunk.com/en?resourceId=CIM_User_CIMfields&version=cim-6_1),
[Elastic ECS event fields](https://www.elastic.co/docs/reference/ecs/ecs-event), and
[Microsoft Sentinel ASIM common fields](https://learn.microsoft.com/azure/sentinel/normalization-common-fields).

Monitor fleet-status security-event diagnostics for pending and terminal counts, oldest pending time, outbox
bytes, dropped events, collector reachability, and last attempt/success/failure. Test collector outage and recovery
before enabling fail-closed thresholds; a threshold breach intentionally blocks new script execution until the
outbox becomes healthy.

## Related

- [Authoritative organization policy](organization-policy.md)
- [Durable audit outbox and remote collectors](audit-outbox.md)
- [Platform administration](README.md)
