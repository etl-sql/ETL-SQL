---
trigger: $xml
label: CREATE CONNECTION … ON XML
description: XML file connection with XPath root element for row extraction
---
CREATE CONNECTION «ConnName» ON XML(
  PATH      = '«path/to/file.xml»',
  ROOT_PATH = '«/Root/Item»'
);
