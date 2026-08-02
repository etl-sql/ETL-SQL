# SET MAX_SMTP_EMAILS_PER_SCRIPT
Sets the anti-spam limit capping the number of emails a single script run may send.

## Syntax
```sql
SET MAX_SMTP_EMAILS_PER_SCRIPT = <n>;
```

## Parameters
- **n** — Maximum emails per script run. Default: 100.

## Example
```sql
-- Allow more emails for a bulk notification script
SET MAX_SMTP_EMAILS_PER_SCRIPT = 500;

FOREACH @row IN #recipients
BEGIN
    SEND EMAIL TO @row.email
        FROM 'reports@example.com'
        SUBJECT 'Monthly Report'
        BODY 'Your report is ready.'
        AT smtp_conn;
END;
```

## Notes
- This is a safety limit to prevent accidental email storms.
- Corresponding `appsettings.json` key: `Security:MaxSmtpEmailsPerScript`.
- Default: 100.

## References
- [SET Commands](README.md)
