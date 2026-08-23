---
trigger: $bookmark
label: CREATE BOOKMARK
description: Author bookmark with parameters, page, and state
---
CREATE BOOKMARK «BookmarkName» AS (
    TITLE = '«Display Title»',
    PARAMETERS (
        @«param» = '«value»'
    ),
    PAGE = «PageName»
);
