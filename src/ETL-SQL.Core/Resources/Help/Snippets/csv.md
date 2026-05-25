---
trigger: $csv
label: CREATE CONNECTION … ON FLATFILE (CSV)
description: Delimited text file connection with CSV defaults
---
CREATE CONNECTION «ConnName» ON FLATFILE(
  PATH      = '«path/to/file.csv»',
  DELIMITER = ',',
  HEADER    = ON
);
