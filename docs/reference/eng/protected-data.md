# `eng.protected_data`

Durable lineage records identified as PII, PHI, PCI, sensitive, confidential, or restricted.

```sql
SELECT * FROM eng.protected_data WHERE Classification = 'restricted';
```

Columns include identity, run, target/source, protection, stewardship, tag, and source-location fields.

## References

- [Lineage](../statements/session-control/lineage.md)
- [Engine Catalog](README.md)
