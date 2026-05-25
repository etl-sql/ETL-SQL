---
trigger: $excel
label: CREATE CONNECTION … ON EXCEL
description: Excel workbook connection with sheet selection
---
CREATE CONNECTION «ConnName» ON EXCEL(
  PATH   = '«path/to/file.xlsx»',
  SHEET  = '«Sheet1»',
  HEADER = ON
);
