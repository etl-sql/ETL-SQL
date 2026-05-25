---
trigger: $blob
label: CREATE CONNECTION … ON AZURE_BLOB
description: Azure Blob Storage connection with account key or SAS connection string
---
CREATE CONNECTION «ConnName» ON AZURE_BLOB(
  ACCOUNT_NAME = '«storageaccount»',
  ACCOUNT_KEY  = '«key»',
  CONTAINER    = '«container-name»'
);
