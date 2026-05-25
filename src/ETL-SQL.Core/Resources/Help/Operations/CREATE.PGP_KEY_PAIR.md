# PGP_KEY_PAIR
Generates an OpenPGP key pair (RSA) and writes the private and public key files to the specified path.

Syntax:
  CREATE PGP_KEY_PAIR 'output_path'
    WITH (
      BITS       = 2048 | 3072 | 4096,
      IDENTITY   = 'User Name <email@example.com>',
      PASSPHRASE = '<passphrase>'
    );

The private key is written to output_path; the public key is written to output_path.pub.

Options:
  BITS        — key size in bits (default 2048; 4096 recommended for high security)
  IDENTITY    — the OpenPGP User ID identity string (required)
  PASSPHRASE  — passphrase to protect the private key (optional)

```sql
-- Generate a 4096-bit PGP key pair
CREATE PGP_KEY_PAIR 'C:\keys\internal_pgp'
  WITH (
    BITS       = 4096, 
    IDENTITY   = 'ETL-SQL Service <etl@company.com>', 
    PASSPHRASE = @pgp_passphrase
  );

-- Use the generated key for file encryption
ENCRYPT FILE 'data.csv' TO 'data.pgp' 
  PGP_KEY 'C:\keys\internal_pgp' 
  PASSWORD @pgp_passphrase;
```

References:
- [Specialized Operations](../../../../../Docs/Reference/Specialized_Operations.md)
