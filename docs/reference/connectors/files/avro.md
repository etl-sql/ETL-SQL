# AVRO
Reads and writes Apache Avro binary files. The schema is either embedded in the file or supplied via SCHEMA_FILE.

Syntax:
  CREATE CONNECTION <name> AS AVRO(
    PATH        = 'file.avro',
    SCHEMA_FILE = 'schema.avsc',
    ENCRYPT     = ON | OFF,
    PASSWORD    = '<passphrase>'
  );

Options:
- **PATH** — file path to the Avro file (required)
- **SCHEMA_FILE** — path to an .avsc JSON schema file; used when the file has no embedded schema
- **ENCRYPT** — encrypt the output file (default OFF)
- **PASSWORD** — passphrase for encryption

```sql
CREATE CONNECTION Events AS AVRO(
  PATH = 'C:\data\events_2024.avro'
);

SELECT event_type, user_id, event_time
  INTO #events
  FROM Events
  WHERE event_type = 'purchase';

PRINT 'Events loaded: ' + @@ROWCOUNT;
```

References:
- [Data Connectors](../../../administration/platform/README.md)
