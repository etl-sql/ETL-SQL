### Added

- Added an operator view of durable remote audit delivery at `GET /api/admin/audit/collector`: queue depth and queued bytes, the age of the oldest undelivered event, terminal failures, last attempt, last success, last error, and the thresholds any of those readings is compared against. These signals already existed in health, Prometheus, and fleet status, which is fine for a dashboard and no use to someone mid-incident deciding whether to raise a threshold or go and fix the collector.

  Fail-closed state is produced by asking `AuditDeliveryGate` itself whether the next mutation would be refused, rather than re-deriving its thresholds. A second copy of that rule would eventually disagree with the one that actually blocks writes, and the operator would be reading a reassurance that is not true.

- Added `POST /api/admin/audit/collector/test-delivery`, which posts a synthetic event to the configured collector through the real delivery path — same endpoint resolution, same authentication, same body shape. A probe that took its own path would prove the probe works, not the delivery. It carries no audit content, reports the endpoint without its query string (which can carry a token), redacts transport failures, and is itself audited.
