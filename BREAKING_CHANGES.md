# Breaking Changes

This file is the canonical record of every behavioral change that could cause an existing script to produce different results or fail to execute. Maintained by the protocol in [AGENTS.md §14](AGENTS.md).

## Format

```
### vX.Y.Z — Category: Short description
- **What changed**: One sentence describing the old vs. new behavior.
- **Who is affected**: Scripts using [syntax / feature / connector].
- **Migration**: What a script author must change.
```

Categories: `Syntax` | `Semantic` | `TypeSystem` | `Runtime` | `Connector` | `Parser`

---

### v0.12.0 — Runtime: Portal administration mutations require optimistic-concurrency versions
- **What changed**: Updates, deletes, ACL changes, and bulk administration operations for users, groups, folders, reports, datasets, subscriptions, SMTP definitions, and scheduled jobs require the version returned by the latest read; stale writes return `409 Conflict` with current state instead of silently overwriting a newer edit.
- **Who is affected**: Report Portal API and UI clients that mutate administrator-managed resources.
- **Migration**: Read the resource first, retain its `Version`/ETag, and send it with the mutation as documented; for bulk operations inspect each per-item result and retry only conflicted items after refreshing their current state.

### v0.12.0 — Runtime: Report Portal enforces a single-active-instance topology
- **What changed**: A portal process now holds an exclusive instance lock beside the portal database. A second process pointed at the same database fails startup. Execution jobs are persisted, active refresh ownership is database-enforced, and jobs abandoned by restart become `Cancelled` with an interruption reason.
- **Who is affected**: Deployments that started multiple Report Portal processes against one SQLite database or storage root.
- **Migration**: Run exactly one portal service instance per portal database and script/snapshot/dataset storage root. Use the separate Orchestrator service for distributed scheduled execution; multi-instance portal hosting remains unsupported until shared gates, sessions, and quotas are implemented.

### v0.11.0 — Runtime: Share links are anonymous creator-authorized capabilities
- **What changed**: Share links now resolve without caller authentication, but share and embed capabilities fail closed when their creator is disabled or loses current report permission; new capabilities expire after seven days by default.
- **Who is affected**: Portal clients that treated share links as authenticated shortcuts or created links/embeds without an explicit expiry.
- **Migration**: Treat the capability URL as a bearer secret, distribute it only to intended viewers, and ensure the creator retains report access; specify `ExpiresAt` when a lifetime other than seven days is required.

### v0.11.0 — Runtime: Portal sessions are invalidated on security changes
- **What changed**: Access tokens now require a current Identity security stamp, refresh tokens are stored as SHA-256 digests and rotate on use, and logout or security-sensitive account/permission changes revoke all sessions for affected users.
- **Who is affected**: Portal API clients holding tokens across role, group, ACL, password, active-state, logout, disconnect, or explicit revocation changes.
- **Migration**: Treat `401 Unauthorized` after a security change as a required reauthentication and replace stored refresh tokens after every successful refresh.

### v0.11.0 — Runtime: Subscription jobs are credential-free delivery triggers
- **What changed**: Scheduled subscription scripts no longer contain export, recipient, parameter, or SMTP credential data; they trigger portal-side delivery that reauthorizes the owner immediately before export and send.
- **Who is affected**: Administrators or integrations that inspect, edit, or execute generated `SUB:` job scripts outside the Report Portal.
- **Migration**: Keep the Report Portal running with access to the Orchestrator database and manage delivery configuration through the subscription and SMTP APIs instead of editing generated scripts.

### v0.10.0 — Runtime: Audit log exports are audited
- **What changed**: Exporting the Portal audit log now records an `EXPORT_AUDIT_LOG` audit event.
- **Who is affected**: Administrators and integrations that export or count Portal audit records.
- **Migration**: Account for the additional audit event when filtering or aggregating audit activity.

### v0.10.0 — Runtime: Subscription creation enforces report visibility
- **What changed**: Creating a Portal subscription now requires `READ` permission on the report's folder.
- **Who is affected**: Portal clients that attempted to create subscriptions for reports the current user could not view.
- **Migration**: Grant the user an appropriate folder permission or create the subscription as an authorized user.

### v0.10.0 — Connector: SMTP attachment MIME types
- **What changed**: SMTP attachments now use the MIME type inferred from their file extension instead of the generic default content type.
- **Who is affected**: Scripts that send attachments through an `SMTP` connection and consumers that inspect attachment MIME metadata.
- **Migration**: Accept the extension-appropriate MIME type, such as `text/csv`, `text/markdown`, or `application/pdf`.

## v1.0.0 (baseline)

All syntax and behavior documented in:
- [`Docs/Reference/Grammar.md`](Docs/Reference/Grammar.md)
- [`Docs/Reference/Data_Connectors.md`](Docs/Reference/Data_Connectors.md)
- [`Docs/Reference/Standard_Library.md`](Docs/Reference/Standard_Library.md)

as of this version constitutes the **v1.0 baseline**. No migration required from prior versions.


---

<!-- Add new entries above this line, most recent version first. -->
