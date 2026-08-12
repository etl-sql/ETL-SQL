---
trigger: $csv
label: CREATE CONNECTION … AS FLATFILE(CSV)
description: Delimited text file connection with CSV defaults
---
CREATE CONNECTION «ConnName» AS FLATFILE(
  PATH      = '«path/to/file.csv»',
  DELIMITER = ',',
  HEADER    = ON,
  TRANSACTIONAL = «ON»
);
