---
trigger: $func
label: CREATE FUNCTION … RETURNS … AS BEGIN … END
description: Named scalar function with typed parameters and return value
---
CREATE FUNCTION «FuncName»(«@param» «INT») RETURNS «INT» AS
BEGIN
  RETURN «@param»;
END;
