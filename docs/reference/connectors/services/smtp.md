# SMTP
Connects to an SMTP mail server for sending email. Used with SEND EMAIL operations and report subscription delivery.

Syntax:
  CREATE CONNECTION <name> AS SMTP(
    HOST         = 'smtp.example.com',
    PORT         = 587,
    USERNAME     = 'user@example.com',
    PASSWORD     = '<password>',
    USE_SSL      = ON | OFF,
    DEFAULT_FROM = 'noreply@example.com'
  );

Options:
- **HOST** — SMTP server hostname (required)
- **PORT** — SMTP port (default 587 for STARTTLS; 465 for SSL)
- **USERNAME** — authentication username
- **PASSWORD** — authentication password
- **USE_SSL** — use SSL/TLS (default ON)
- **DEFAULT_FROM** — default From address when not specified in SEND EMAIL

```sql
CREATE CONNECTION MailServer AS SMTP(
  HOST         = 'smtp.corp.local',
  PORT         = 587,
  USERNAME     = @smtp_user,
  PASSWORD     = @smtp_pass,
  USE_SSL      = ON,
  DEFAULT_FROM = 'etl@corp.local'
);

SEND EMAIL
  TO      = 'team@corp.local',
  SUBJECT = 'Daily Report — ' + FORMAT(GETDATE(), 'yyyy-MM-dd'),
  BODY    = 'Report attached.',
  ATTACH  = 'C:\reports\daily.xlsx'
  AT MailServer;
```

References:
- [Data Connectors](../../../guides/administration.md)
