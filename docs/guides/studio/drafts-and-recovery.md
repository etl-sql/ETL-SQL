# Studio Drafts, Publishing and Recovery

[« Back to Studio Guides](README.md)

**Applies to v0.21.0.** Save, recovery, and publishing have different purposes. A dirty indicator
means the buffer has unsaved edits; it does not prove that the host kept a recovery copy.

## Where the text is kept

| Kind | Storage and visibility | What it permits |
| :--- | :--- | :--- |
| Portal private draft | Portal storage, scoped to your tenant and owner identity; listed under **My Private Drafts**. Other callers, including administrators through Portal APIs, cannot read it. | Save unpublished work without a publish capability or writable folder. Permitted previews use your existing capabilities; it cannot be an Orchestrator pipeline run or schedule. |
| Portal catalog document | Catalog storage with document/folder grants. Publishing does not make it public to everyone. | Report viewing and saved-pipeline execution according to separate permissions. |
| Recovery draft | Host copy of unsaved edits associated with your document/workspace. | Offer to restore text after a crash or renewed sign-in. It does not publish or save a new catalog/file version. |
| Desktop saved file | Your opened workspace, under your OS account and filesystem permissions. | Keep and source-control the local `.rptsql` or `.etlsql` file. |

Studio does not store script text in browser storage. Portal owns its drafts; the local host keeps
recovery drafts in your OS user's local application data. Recovery requires a successful host save
of those edits. Offline, disabled, expired, or refused recovery is unavailable; text can still exist
only in the open buffer. Save while you can and preserve that buffer before reloading or signing out.

Plaintext credential assignments are refused for private and recovery drafts. Encrypt credentials
or use the supported secret mechanism; do not paste passwords into a support transcript. Read the
[connection/security guide](../patterns/troubleshooting-connections-and-security.md).

## Keep and reopen a private draft

If Portal permits private drafts, **Save** on a new unpublished document can keep it as your private
draft. Supply its name and wait for confirmation. Reopen it from **My Private Drafts** on Studio Home.
The draft's report/pipeline kind is fixed at creation. If your deployment disables this feature,
Studio cannot promise that unpublished text is kept.

Default Portal policy allows 25 private drafts per owner, keeps a draft for 90 days after its last
save, and retains the last 10 saved texts. Operators can change these values or disable private
drafts with `Portal:Studio` settings. Catalog recovery drafts expire after seven days by default.
The host's actual policy and displayed expiry take precedence over these defaults.

## Publish a private draft

1. Save the draft's latest text. Resolve unsaved edits or version conflicts first.
2. Choose **Publish**, then a permitted destination folder and document name.
3. Publishing needs script-save and report-publish capabilities and **Manage** on that folder.
   A private draft's owner identity alone does not grant publishing.
4. On success, the saved text becomes a catalog document and the private draft is removed.
   A refusal leaves the private draft intact. Check the catalog document before handing it off.

Readers need access to the resulting catalog report; unpublished drafts cannot be shared through
reader links. Publishing retains the latest saved text, while the audit records prior draft revision
hashes and times. Earlier private-draft texts do not become catalog version history; copy out any
earlier text you need before publishing.

## Recover edits and resolve conflicts

Reopen the same document as the same authenticated user. When kept unsaved edits are available,
Studio offers **Restore**. Restored text is dirty and must be reviewed and saved.

- If the saved document changed meanwhile, the restore offer explains that it puts your edits over
  the newer version. Preserve both texts and reconcile them before saving. A stale version or source
  revision is refused; Studio must not silently overwrite the newer document.
- Catalog edits use an edit lease. If another author owns it or renewal fails, saving can become
  unavailable even while your buffer remains editable. Reopen/reacquire as offered, then review any
  newer saved version. Do not treat a recovery copy as ownership of the lease.
- On desktop, an external file change is also a revision conflict. Keep your dirty buffer and compare
  it with the file before deciding what to save. A moved/deleted Portal selection needs a catalog
  **Refresh** and a new open; refresh does not replace other dirty documents.

## Retry an editor or session failure

| Symptom | What to do |
| :--- | :--- |
| Basic textarea / degraded editor | Keep typing if needed, then use the editor's **Retry**. Both buffers and edits made during loading are retained. Formatting, visual/helper writes, lint and cursor planning need the full editor. |
| Portal startup error or timeout | Check the connection and use **Retry Connection**. Session/catalog startup has a 15-second deadline; failure grants no fallback permissions. |
| Expired sign-in / failed refresh | Sign in again with the intended identity, reopen the document, and use Restore if the host kept edits. Do not assume an unacknowledged buffer survived. |
| Analysis rate limited | Keep the text. Diagnostics explain that they are stale; analysis retries the current buffer after the permitted delay. |
| Document above its character ceiling | Keep the text editable. Reduce it below the displayed limit to resume automatic editor services. Oversized Portal saves are refused without truncating the buffer. |
| Run outcome unknown / Stop unconfirmed | Keep the run ID and consult run history before running again. See [Run and schedule](run-and-schedule.md#follow-and-stop-a-portal-run). |

For support, record the release version, host, document name/ID, saved version, run ID when present,
visible diagnostic, and the action that failed. Leave out authenticated desktop URLs, tokens,
connection strings, passwords, and raw secret values.

## Desktop policy limits

The local Studio host is trusted local execution, not a hostile-code sandbox. Workspace file access
stays within the opened project; read-only workspace mode disables file saves and new recovery-draft writes.
Local recovery defaults to seven days and is scoped to the OS user, workspace, and file path.

Desktop runs have a 60-second timeout, a 128 MB operator memory grant, a 256 MB session ceiling,
and at most 1,000 returned rows. Automatic editor services pause above 200,000 UTF-16 characters;
Portal advertises its configured character ceiling, also 200,000 by default. Helper previews are
read-only and bounded; desktop helper result rows have a 1 MiB serialized byte budget, and native
helper preview currently requires a direct SQLite connection in read-only mode. These controls are
not permission to execute arbitrary untrusted SQL or proof that a displayed sample is complete.

References: [Portal configuration](../../administration/platform/README.md),
[recovery evidence](../../architecture/decisions/studio-failed-recovery-evidence.md),
[delivery/document measurements](../../benchmarks/studio-delivery-budgets.md),
[SQLite reference](../../reference/connectors/databases/sqlite.md).
