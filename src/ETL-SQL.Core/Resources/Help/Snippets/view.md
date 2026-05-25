---
trigger: $view
label: CREATE VIEW … AS
description: Session-scoped view that wraps a query as a reusable named alias
---
CREATE VIEW «ViewName» AS
SELECT «col1», «col2»
FROM «source»
WHERE «condition»;
