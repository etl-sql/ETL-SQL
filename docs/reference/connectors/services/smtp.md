# SMTP

Outbound-only email connector used with the `SEND EMAIL` statement and report subscription delivery.

Aliases: `EMAIL`

## Options

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `PORT` | SMTP server port (default: `25`) | No |
| `USERNAME` | Authentication username | No |
| `PASSWORD` | Authentication password | No |
| `USE_SSL` | Enable TLS/SSL (`TRUE`/`FALSE`, default: `FALSE`) | No |
| `DEFAULT_FROM` | Default sender address when `FROM` is omitted in `SEND EMAIL` | No |

The host is supplied as the traditional connection-string argument (e.g. `SMTP('smtp.example.com', …)`).

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

## References

- [Service Connectors](README.md)
- [Connectors](../README.md)
- [SEND EMAIL](../../file-operations/send-email.md)
