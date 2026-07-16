---
trigger: $smtp
label: CREATE CONNECTION … ON SMTP
description: SMTP connection for sending email notifications
---
CREATE CONNECTION «ConnName» AS SMTP(
  HOST         = '«smtp.example.com»',
  PORT         = '«587»',
  USERNAME     = '«user@example.com»',
  PASSWORD     = '«password»',
  USE_SSL      = TRUE,
  DEFAULT_FROM = '«sender@example.com»'
);
