# SMTP

Outbound-only email connector used with the `SEND EMAIL` statement and report subscription delivery.

Aliases: `EMAIL`

## Options

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `PORT` | SMTP server port (default: `25`) | No |
| `USERNAME` | Authentication username | No |
| `PASSWORD` | Authentication password | No |
| `USE_SSL` | Require TLS (`TRUE`/`FALSE`, default: `FALSE`). `TRUE` uses implicit TLS on port 465 and required STARTTLS on any other port; `FALSE` uses STARTTLS only when the server offers it | No |
| `DEFAULT_FROM` | Default sender address when `FROM` is omitted in `SEND EMAIL` | No |

The host is supplied as the traditional connection-string argument (e.g. `SMTP('smtp.example.com', …)`).

## Authentication

SMTP email connector supports:
- **Authenticated SMTP**: Supply `USER` and `PASSWORD`.
- **Anonymous Relay**: Omit credentials for trusted internal relays.

## Examples

```sql
-- Gmail with TLS
CREATE CONNECTION mailer AS SMTP('smtp.gmail.com', PORT=587, USERNAME='alerts@example.com', PASSWORD='apppassword',
         USE_SSL=TRUE, DEFAULT_FROM='alerts@example.com');

SEND EMAIL
    TO 'ops@example.com'
    FROM 'alerts@example.com'
    SUBJECT 'Nightly Load Complete'
    BODY 'All records processed.'
    AT mailer;
```

## Troubleshooting

- **Authentication Failed**: For Office 365 / Gmail, use App Passwords or OAuth2 relay.
- **TLS Handshake Error**: With `USE_SSL=TRUE`, use port 465 for implicit TLS or 587 for STARTTLS; a server that offers neither needs `USE_SSL=FALSE`.

## References

- [Service Connectors](README.md)
- [Connectors](../README.md)
- [SEND EMAIL](../../file-operations/send-email.md)
