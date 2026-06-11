---
trigger: $export-dataset
label: EXPORT DATASET portable copy
description: Export a portal dataset using a non-persisted PASSWORD transport credential
---
EXPORT DATASET &«dataset»
TO '«C:\Transfer\dataset.parquet»'
ENCRYPT = PASSWORD
PASSWORD = '«transport-secret»';
