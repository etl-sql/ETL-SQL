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
