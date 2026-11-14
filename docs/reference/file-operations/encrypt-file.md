# ENCRYPT / DECRYPT FILE

Encrypts or decrypts a local file with a password, an SSH RSA key or a PGP key, or every file in a
directory tree with a password.

## Syntax

```sql
-- Files
ENCRYPT FILE '<source>' TO '<destination>'
    { PASSWORD '<passphrase>' | KEYFILE '<rsa_public_key.pem>' | PGP_KEY '<public_key.asc>' }
    [WITH (OVERWRITE = ON | OFF)];

DECRYPT FILE '<source>' TO '<destination>'
    { PASSWORD '<passphrase>'
    | KEYFILE '<rsa_private_key.pem>' [PASSWORD '<key_passphrase>']
    | PGP_KEY '<private_key.asc>' [PASSWORD '<key_passphrase>'] }
    [WITH (OVERWRITE = ON | OFF)];

-- Directories (password only)
ENCRYPT DIRECTORY '<source_dir>' TO '<destination_dir>' PASSWORD '<passphrase>' [WITH (OVERWRITE = ON | OFF)];
DECRYPT DIRECTORY '<source_dir>' TO '<destination_dir>' PASSWORD '<passphrase>' [WITH (OVERWRITE = ON | OFF)];
```

`PASSWORD` also takes the parenthesized form `PASSWORD('<passphrase>')`. Any value can be a variable.

## Semantics

| Method | `ENCRYPT FILE` uses | `DECRYPT FILE` needs | Format |
| :--- | :--- | :--- | :--- |
| `PASSWORD` | the passphrase | the same passphrase | AES-256 with an HMAC-SHA256 tag; the key is derived with PBKDF2-SHA256 (600,000 iterations, random salt). ETL-SQL's own format: decrypt it with `DECRYPT FILE`. |
| `KEYFILE` | a PEM RSA **public** key | the matching PEM RSA **private** key, plus `PASSWORD` if that key is encrypted | A random AES-256 key, wrapped with RSA-OAEP-SHA256. ETL-SQL's own format. |
| `PGP_KEY` | an OpenPGP **public** key | the matching OpenPGP **private** key, plus `PASSWORD` for its passphrase | Standard OpenPGP, readable by GnuPG and other PGP tools. |

- Give exactly one method. `KEYFILE` with `PGP_KEY`, or `PASSWORD` with a key on `ENCRYPT FILE`, is a
  syntax error, because the statement would otherwise use one and ignore the other.
- [CREATE PGP_KEY_PAIR](create-pgp-key-pair.md) writes `public.asc` and `private.asc` into a folder.
  [CREATE SSH_KEY_PAIR](create-ssh-key-pair.md) with `ALGORITHM = 'RSA'` writes `id_rsa` and
  `id_rsa.pub`. An ECDSA key cannot be used with `KEYFILE`.
- `TO` is required. Without it nothing is written.
- The source file is left in place. Remove the plaintext with `DELETE FILE` once the encrypted file is
  written (see the vendor-drop example).
- `ENCRYPT DIRECTORY` encrypts every file, subfolders included, and writes each one as `<name>.enc`
  under the destination with the same folder layout. `DECRYPT DIRECTORY` decrypts only `.enc` files
  and removes that suffix.
- The script's master password (`USE PASSWORD` or `--pass`) protects connection strings and secrets
  in the script. It is never used to encrypt or decrypt files: a file passphrase has to travel with
  the file, so give `PASSWORD` on the statement.

## Options

- **`OVERWRITE`** — `ON` (default) replaces an existing destination; `OFF` fails instead.

Any other `WITH` option is a syntax error that lists the accepted options.

## Examples

### 1. Password round trip

```sql
DECLARE @pass VARCHAR = 'correct-horse-battery-staple';

ENCRYPT FILE 'staging/payroll.csv' TO 'staging/payroll.csv.enc' PASSWORD @pass;
DELETE FILE 'staging/payroll.csv';

DECRYPT FILE 'staging/payroll.csv.enc' TO 'staging/payroll.csv' PASSWORD @pass WITH (OVERWRITE = OFF);
```

### 2. Vendor drop: compress, encrypt to the partner's PGP key, send, clean up

```sql
CREATE CONNECTION sftp_drop AS SFTP(HOST='sftp.partner.com', USER='partner_agent', KEYFILE='certs/sftp_id.rsa');

DECLARE @local_csv VARCHAR = 'staging/partner_feed.csv';
DECLARE @local_zip VARCHAR = 'staging/partner_feed.zip';
DECLARE @local_pgp VARCHAR = 'staging/partner_feed.zip.pgp';

-- Compress first: encrypted data does not compress
COMPRESS FILE @local_csv TO @local_zip;

-- Encrypt to the partner's public key; only they can decrypt it
ENCRYPT FILE @local_zip TO @local_pgp PGP_KEY 'certs/partner_public.asc';

SEND FILE @local_pgp TO '/inbound/partner_feed.zip.pgp' AT sftp_drop WITH (OVERWRITE = TRUE);

-- Remove every local copy, plaintext included
DELETE FILE @local_csv;
DELETE FILE @local_zip;
DELETE FILE @local_pgp;
```

### 3. Decrypt a file a partner encrypted to your PGP key

```sql
DECRYPT FILE 'inbound/orders.csv.pgp' TO 'staging/orders.csv'
    PGP_KEY 'certs/our_private.asc' PASSWORD 'private-key-passphrase';
```

### 4. SSH RSA key pair

```sql
CREATE SSH_KEY_PAIR 'certs/ledger' WITH (BITS = 4096, ALGORITHM = 'RSA');

ENCRYPT FILE 'staging/ledger.csv' TO 'staging/ledger.csv.enc' KEYFILE 'certs/ledger/id_rsa.pub';
DECRYPT FILE 'staging/ledger.csv.enc' TO 'restore/ledger.csv' KEYFILE 'certs/ledger/id_rsa';
```

### 5. A whole directory

```sql
ENCRYPT DIRECTORY 'exports/2026-11/' TO 'vault/2026-11/' PASSWORD 'archive-passphrase';
DECRYPT DIRECTORY 'vault/2026-11/' TO 'restore/2026-11/' PASSWORD 'archive-passphrase';
```

## Guardrails

- **Compress before encrypting.** Compressing encrypted data saves nothing; the linter warns when
  `COMPRESS FILE` follows `ENCRYPT FILE` on the same path.
- **Delete the plaintext yourself.** Encryption never removes the source.
- **Keep passphrases out of saved scripts.** A literal password is visible to anyone who can read the
  script. Prefer an `ENC:` value or a prompted password; see [USE](../variables-parameters/use.md).
- Destination paths pass the same file-system policy and file-type checks as every other write.

## Errors

- **`ENCRYPT FILE requires a PASSWORD, KEYFILE or PGP_KEY clause.`** — no method was given. Add one;
  the master password is not a fallback.
- **`Destination file already exists and OVERWRITE is OFF: <path>`** — remove the file or set
  `OVERWRITE = ON`.
- **`ENCRYPT FILE takes KEYFILE (an SSH RSA key) or PGP_KEY, not both.`** — pick one method.
- **`ENCRYPT FILE with PGP_KEY encrypts to a public key and takes no PASSWORD.`** — drop `PASSWORD`;
  it is only the private-key passphrase on `DECRYPT FILE`.
- **`ENCRYPT DIRECTORY does not support KEYFILE or PGP_KEY.`** — directories use `PASSWORD` only.
- **`ENCRYPT FILE has no WITH option '<key>'.`** — the message lists the accepted options.
- **`Could not decrypt '<file>': the password is wrong, or the file was changed after it was encrypted.`**
- **`Could not decrypt '<file>': the private key does not match the public key the file was encrypted to.`**
  — the `KEYFILE` is not the partner of the key used to encrypt.
- **`Could not decrypt '<file>': it was not encrypted with PASSWORD by ETL-SQL.`** (or `KEYFILE`) —
  the file is plain text or used another method; decrypt it with the method that encrypted it.
- PGP failures name the cause: no matching private key (with the key IDs the file was encrypted
  to), a wrong passphrase, or input that is not PGP data.
- A failed decrypt writes no output and leaves an existing destination unchanged. Messages never
  include passwords or key contents.

## References

- [File Operations Reference](README.md)
- [COMPRESS FILE](compress-file.md)
- [SEND FILE](send-file.md)
- [RECEIVE FILE](receive-file.md)
- [ETL Cookbook: Secure Vendor Handshake](../../cookbooks/etl/secure-vendor-handshake.md)
- [ETL Cookbook: Automated SFTP Bursting](../../cookbooks/etl/automated-sftp-bursting.md)
- [Syntax Index](../../syntax-index.md)
