---
trigger: $excel
label: CREATE CONNECTION … ON EXCEL
description: Excel workbook connection with sheet selection
---
CREATE CONNECTION «ConnName» AS EXCEL(
  PATH   = '«path/to/file.xlsx»',
  SHEET  = '«Sheet1»',
  HEADER = ON,
  TRANSACTIONAL = «ON»
);
