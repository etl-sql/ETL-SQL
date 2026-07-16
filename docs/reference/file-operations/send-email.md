Sends an email via an SMTP connection.

VERBOSE:
  SEND EMAIL
    TO 'recipient@example.com'
    FROM 'sender@example.com'
    SUBJECT 'Subject line'
    BODY 'Message body'
    [AT connectionName]
    [CC ['cc1@example.com', ...]]
    [BCC ['bcc1@example.com', ...]]
    [ATTACH ['path/to/file', ...]]

SHORTHAND:
  SEND EMAIL(connectionName, 'to', 'from', 'subject', 'body'[, cc][, bcc][, attachments])

Parameters:
  TO      - Recipient email address
  FROM    - Sender email address
  SUBJECT - Email subject line
  BODY    - Email body (plain text or HTML)
  CC/BCC  - Optional carbon-copy and blind carbon-copy recipients
  ATTACH  - Optional list of local file paths to attach

Examples:
  SEND EMAIL TO 'user@corp.com' FROM 'etl@corp.com' SUBJECT 'Report' BODY 'See attached.' AT MySmtp;
  SEND EMAIL(MySmtp, 'user@corp.com', 'etl@corp.com', 'Report', 'See attached.');

References:
- [Specialized Operations](../../guides/administration.md)
