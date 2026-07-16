---
trigger: $json
label: CREATE CONNECTION … ON JSON
description: JSON file connection with optional root path for nested arrays
---
CREATE CONNECTION «ConnName» AS JSON(
  PATH      = '«path/to/file.json»',
  ROOT_PATH = '«$»'
);
