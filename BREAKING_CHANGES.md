# Breaking Changes

This file is the canonical record of every behavioral change that could cause an existing script to produce different results or fail to execute. Maintained by the protocol in [AGENTS.md §16](AGENTS.md#16-breaking-change-protocol).

## Format

```
### vX.Y.Z — Category: Short description
- **What changed**: One sentence describing the old vs. new behavior.
- **Who is affected**: Scripts using [syntax / feature / connector].
- **Migration**: What a script author must change.
- **Diagnostic**: Stable compatibility diagnostic code, or `N/A` when this is not detectable by linting.
- **Earliest removal**: Version where the deprecated behavior may be removed, or `N/A` for immediate breaking changes.
```

Categories: `Syntax` | `Semantic` | `TypeSystem` | `Runtime` | `Connector` | `Parser`

---

### v0.19.0 — Semantic: Invalid detail surfaces (TOOLTIP) now fail the build
- **What changed**: A `TOOLTIP` clause whose target could not be resolved — a missing container or visual, a container cycle, a nested detail surface, or a surface over its depth/visual/node/refresh/payload budget — previously still produced a manifest, which every renderer then ignored. The report published and the tooltip simply never appeared. Manifest building now rejects the report with an `RPT21xx` diagnostic instead.
- **Who is affected**: Reports whose `TOOLTIP` clause already pointed at something that does not exist or cannot be rendered. Such a report was already not showing its tooltip; the change is that the failure is now reported instead of silent.
- **Migration**: Correct or remove the named target. The diagnostic names the offending object and what to do about it.
- **Diagnostic**: `RPT2101`–`RPT2110` (missing container, missing visual, cycle, nested surface, depth, visual count, node count, refresh-query count, payload bytes, per-report surface count).
- **Earliest removal**: Immediate.

### v0.19.0 — Semantic: Detail popovers require an explicit, non-secret row-context mapping
- **What changed**: Opening a detail popover pushes one value from the activated row into `@hover_value`. The browser previously fell back to the first column when the visual declared none of the row-context mapping roles. That fallback is removed, and a secret-bearing column is now rejected outright.
- **Who is affected**: Visuals with a container or inline-`VISUALS` `TOOLTIP` that declare none of `X`, `LABEL`, `NAME`, `REGION`, or `Y` in `MAPPINGS`, or that map a column whose name indicates a credential.
- **Migration**: Add one of the row-context mapping roles naming the column that should reach `@hover_value`. If that column is secret-bearing, map a non-secret identifier or label instead; secret values must not reach refresh parameters, manifests, URLs, accessibility text, snapshots, or exports.
- **Diagnostic**: `RPT2114` (no explicit row-context mapping), `RPT2111` (secret-bearing column).
- **Earliest removal**: Immediate.

### v0.19.0 — Semantic: The formatter no longer deletes a visual's TOOLTIP clause
- **What changed**: `CREATE VISUAL` formatting omitted the `TOOLTIP` clause entirely, so formatting a report silently deleted the author's detail surface. Pages, containers, and buttons already formatted theirs. The clause is now emitted.
- **Who is affected**: Anyone who formats a `.rptsql` file containing a visual with a `TOOLTIP` — through the LSP, the designer, or `AstSerializer` directly. Formatted output changes, and previously lost clauses are now preserved.
- **Migration**: None. This restores authored content that was being discarded; re-add any `TOOLTIP` clause an earlier format pass removed.
- **Diagnostic**: N/A — detectable only by comparing formatted output against the source.
- **Earliest removal**: N/A.

### v0.17.0 — Connector: SFTP rejects unpinned host keys by default
- **What changed**: The SFTP connector previously connected and logged a warning when `HOST_KEY_FINGERPRINT` was unset, trusting whatever host key the server presented. It now rejects the connection unless the new `ALLOW_UNPINNED_HOST_KEY` option is set to `TRUE`. A fingerprint that is set but does not match is still rejected, as before.
- **Who is affected**: Scripts using the `SFTP` (or `SSH`) connector without `HOST_KEY_FINGERPRINT`.
- **Migration**: Pin the server key — `HOST_KEY_FINGERPRINT = 'SHA256:...'`, obtained from `ssh-keygen -lf <server_host_key>` — which is the recommended fix and gives man-in-the-middle protection. Where an unverified connection is genuinely intended (trusted network, migration window), set `ALLOW_UNPINNED_HOST_KEY = 'TRUE'` to restore the previous behavior explicitly.
- **Diagnostic**: Runtime connection failure logging that the host key is not pinned, naming both options.
- **Earliest removal**: Immediate.

### v0.14.0 — Security: Removal of generic .tmp from whitelisted file extensions
- **What changed**: The `.tmp` file extension has been removed from the whitelist of allowed extensions in `SecurityService`. Files with the `.tmp` extension are no longer readable or writable by default.
- **Who is affected**: Scripts that explicitly read from or write to `.tmp` files.
- **Migration**: Use `.txt` or other allowed extensions (e.g. `.dat`, `.csv`) for temporary script-driven operations.
- **Diagnostic**: Linter or runtime errors when accessing `.tmp` files.
- **Earliest removal**: Immediate.

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
- [`docs/guides/onboarding/getting-started.md`](docs/guides/onboarding/getting-started.md)
- [`docs/administration/platform/README.md`](docs/administration/platform/README.md)
- [`docs/guides/onboarding/getting-started.md`](docs/guides/onboarding/getting-started.md)

as of this version constitutes the **v1.0 baseline**. No migration required from prior versions.


---

<!-- Add new entries above this line, most recent version first. -->
