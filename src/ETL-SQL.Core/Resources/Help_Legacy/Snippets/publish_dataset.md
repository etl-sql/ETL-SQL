---
trigger: $publish-dataset
label: PUBLISH DATASET into portal
description: Import a portable dataset export and re-encrypt it with the portal at-rest key
---
PUBLISH DATASET
FROM '«C:\Transfer\dataset.parquet»'
AS &«dataset_name»
INTO '«/Finance/Imported»'
ACCESS PRIVATE
ENCRYPT = PASSWORD
PASSWORD = '«transport-secret»';
