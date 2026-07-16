---
trigger: $tbl
label: CREATE VISUAL … AS TABLE
description: Paginated, sortable data table
---
CREATE VISUAL «VisualName» AS TABLE (
  SOURCE   = («SELECT * FROM #data»),
  OPTIONS  (PAGE_SIZE = 25, STRIPED = ON, SEARCH = ON)
);
