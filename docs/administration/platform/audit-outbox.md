# Durable Audit Outbox and Remote Collectors

Portal audit rows are written with a durable outbox row in the same database transaction. Configure remote
forwarding under `Portal:Audit:*`:

```json
{
  "Portal": {
    "Audit": {
      "TransportEndpoint": "https://siem.example.com/etl-sql/audit",
      "TransportBearerToken": "ENC:ENCRYPTED_COLLECTOR_TOKEN",
      "TransportBatchSize": 100,
      "TransportIntervalSeconds": 30,
      "TransportTimeoutSeconds": 10,
      "TransportMaxAttempts": 8,
      "TransportLockSeconds": 120,
      "OutboxBackpressureLimit": 10000,
      "OutboxMaxBytes": 104857600,
      "OutboxDeliveredRetentionMinutes": 1440,
      "RequireRemoteDelivery": true,
      "FailClosedMaxPendingBacklog": 1000,
      "FailClosedMaxBacklogSeconds": 900
    }
  }
}
```

The collector endpoint must be HTTPS. Each POST body has an `events` array. Every event includes a stable
`EventId`, audit metadata, and a redacted JSON payload; collectors should treat `EventId` as the deduplication key
because a row may be resent after a crash or lost delivery acknowledgement. Any 2xx response marks the batch
delivered. Non-2xx responses retry with exponential backoff until `TransportMaxAttempts`, then the row is marked
`Failed`.

`RequireRemoteDelivery` changes the Portal from best-effort forwarding to fail-closed mutation behavior. **Leaving it
unset is the recommended default**: fail-closed then turns on automatically for an **enrolled** deployment that has a
collector configured (`TransportEndpoint`), and stays off for standalone/unenrolled deployments and for any deployment
with no collector — so a compliance deployment gets fail-closed audit without having to remember to flip a switch,
while nothing is ever blocked where remote audit was not set up. Set an explicit `true`/`false` to override; an
explicit value always wins. When it is
enabled, security-sensitive mutations are blocked with HTTP 503 once remote audit delivery is judged unavailable:
any terminally failed outbox row, pending backlog over `FailClosedMaxPendingBacklog`, oldest pending row older than
`FailClosedMaxBacklogSeconds`, or queued payload over `OutboxMaxBytes`. Leave it disabled unless an HTTPS collector
is configured, monitored, and treated as mandatory infrastructure.

When `RequireRemoteDelivery` is disabled, the outbox transport may shed old delivered rows and then oldest queued
rows to keep local disk usage under `OutboxMaxBytes`; the durable local `AuditLog` rows remain. When
`RequireRemoteDelivery` is enabled, ETL-SQL never drops queued remote-audit rows to satisfy the cap; it blocks new
mutations until the collector drains the backlog.

Operational checks:

1. Configure the collector and verify it accepts HTTPS POSTs from every Portal node.
2. Trigger a harmless audited action and confirm the collector receives an event with a stable `EventId`.
3. Temporarily stop the collector and confirm pending outbox rows accumulate.
4. If `RequireRemoteDelivery` is enabled, confirm mutations fail with HTTP 503 after the configured backlog, age, or size threshold.
5. Restart the collector and confirm pending rows drain and mutations resume.

## Related

- [Central security events and SIEM delivery](security-events.md)
- [Authoritative organization policy](organization-policy.md)
- [Platform administration](README.md)
