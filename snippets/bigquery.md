---
trigger: $bigquery
label: CREATE CONNECTION … ON BIGQUERY
description: Google BigQuery connection with service account credentials
---
CREATE CONNECTION «ConnName» AS BIGQUERY(
  PROJECT_ID      = '«my-gcp-project»',
  DATASET         = '«my_dataset»',
  CREDENTIAL_FILE = '«path/to/service-account.json»',
  LOCATION        = 'US'
);
