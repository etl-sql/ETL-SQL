### Added

- The Portal data-quality queue can now read the rows behind a quarantine capture, where previously
  every target was view-only. This means the web tier opens the source connection and returns raw
  captured data, so it is gated four ways and the queue names the first gate that stops it:

  1. **The capture must have recorded its provenance.** Captures now write the shared-connection
     alias, connector type, and a catalog-backed flag into the replay manifest at the moment the
     rows are written. Portal never works out where a target lives after the fact — that would mean
     opening a production connection on an inference. The fields are nullable and appended, so
     manifests written by an older engine still deserialize; absent provenance classifies the target
     as view-only, which is what every pre-existing capture gets.
  2. **`Portal:DataQuality:AllowConnectionPreview` must be on**, and it defaults to **off**.
     Upgrading never silently starts opening production connections from the web tier.
  3. **The caller must hold a grant on that shared connection.** `DataSteward` gates the feature;
     the connection ACL gates the data. Steward access alone is deliberately not sufficient —
     quarantined rows are raw source rows carrying whatever the source carried, and letting one
     capability stand in for a grant creates an authority that accumulates implicitly and cannot be
     revoked where it was granted.
  4. **The capture must be self-consistent.** A manifest whose target names one alias while its
     provenance records another is refused rather than reconciled; picking either one would be wrong.

  The connection Portal opens is the manifest's, resolved as `SHARED:<alias>` — never an alias taken
  from the request — so policy, secret resolution, and redaction apply exactly as they do to any
  script using that connection, and the engine's own catalog authorization still runs underneath.
  A missing, disabled, and ungranted connection share one wording on purpose: the catalog does not
  disclose the existence of connections a caller cannot use.

  Every successful read is audited as `READ_QUARANTINE_ROWS` with the target, connection, status
  filter, and row limit. Reading production data is a data-access event, not a page view. The
  existing row cap, 15-second timeout, caller execution identity (so row-level security and PII
  controls apply unchanged), and error redaction are all preserved.

  The queue listing and the row endpoint resolve readability through the same code path, so the list
  cannot offer a row editor that the row endpoint then refuses.

### Changed

- Session-local (`#temp`) quarantine targets keep their existing view-only reason. They are the case
  worth stating: the manifest outlives the run but the table does not, and a preview session
  auto-creates the table empty — a steward offered a row editor would read "no rows" as "nothing was
  quarantined".
