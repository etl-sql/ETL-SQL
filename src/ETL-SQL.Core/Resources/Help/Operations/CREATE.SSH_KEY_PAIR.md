# SSH_KEY_PAIR
Generates an RSA or ECDSA SSH key pair and writes the private and public key files to the specified path.

Syntax:
  CREATE SSH_KEY_PAIR 'output_path'
    WITH (
      BITS       = 2048 | 4096,
      ALGORITHM  = 'RSA' | 'ECDSA',
      PASSPHRASE = '<passphrase>'
    );

The private key is written to output_path; the public key is written to output_path.pub.

Options:
  BITS        — key size in bits for RSA (default 2048; use 4096 for higher security)
  ALGORITHM   — RSA (default) or ECDSA
  PASSPHRASE  — passphrase to protect the private key (optional)

```sql
-- Generate a 4096-bit RSA key pair
CREATE SSH_KEY_PAIR 'C:\keys\partner_deploy'
  WITH (BITS = 4096, ALGORITHM = 'RSA', PASSPHRASE = @key_passphrase);

-- Use the generated key for SFTP
CREATE CONNECTION DeployServer ON SFTP(
  HOST    = 'sftp.partner.com',
  USER    = 'deploy',
  KEYFILE = 'C:\keys\partner_deploy'
);
```
