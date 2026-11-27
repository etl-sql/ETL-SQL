# SIGN FILE / VERIFY FILE SIGNATURE

Creates or verifies a detached ASCII-armored OpenPGP signature (`.sig`) for a local file without encrypting it. Use this when a partner or downstream system expects plaintext data paired with a separate `.sig` file.

## Syntax

```sql
SIGN FILE '<source>' [TO [DIRECTORY] '<destination>']
    PGP_KEY '<your_private_key.asc>' [PASSWORD '<key_passphrase>']
    [WITH (OVERWRITE = ON | OFF)];

VERIFY FILE SIGNATURE '<source>' [SIGNATURE '<signature_file>']
    PGP_KEY '<sender_public_key.asc>';
```

## Options

- **`PGP_KEY`** — Required. On `SIGN FILE`, path to your OpenPGP **private** key; on `VERIFY FILE SIGNATURE`, path to the sender's OpenPGP **public** key.
- **`PASSWORD`** — Optional passphrase for an encrypted `PGP_KEY` private key on `SIGN FILE` (also accepts `PASSWORD('<passphrase>')`).
- **`TO [DIRECTORY] '<destination>'`** — Optional output path on `SIGN FILE`. When omitted, writes `<source>.sig` beside `<source>`. When `TO DIRECTORY '<dir>'` is given, writes `<dir>/<source_filename>.sig`.
- **`SIGNATURE '<signature_file>'`** — Optional path to the detached `.sig` file on `VERIFY FILE SIGNATURE`. When omitted, reads `<source>.sig`.
- **`OVERWRITE`** — `ON` (default) replaces an existing `.sig` destination on `SIGN FILE`; `OFF` fails instead.

## Examples

```sql
-- Sign a file with a detached .sig beside it (exports/ledger.csv.sig)
SIGN FILE 'exports/ledger.csv'
    PGP_KEY 'certs/our_private.asc' PASSWORD @our_key_passphrase;

-- Verify a partner's file against its detached signature
VERIFY FILE SIGNATURE 'inbound/orders.csv'
    SIGNATURE 'inbound/orders.csv.sig'
    PGP_KEY 'certs/partner_public.asc';
```

## Errors

- Fails closed if the source file, signature file, or key file is missing, if the file was modified after signing, or if the signature was created by a different key.
- Never echoes key material or passphrases in error messages, and never leaves a partial `.sig` file on failure.
- Both statements operate on local paths only; transfer the file and `.sig` separately with `SEND FILE` / `RECEIVE FILE` or `COPY FILE`.

## References

- [File Operations Reference](README.md)
- [ENCRYPT / DECRYPT FILE](encrypt-file.md)
- [CREATE PGP_KEY_PAIR](create-pgp-key-pair.md)
- [Syntax Index](../../syntax-index.md)
