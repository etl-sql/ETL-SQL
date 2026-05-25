---
trigger: $portal_folder
label: EXECUTE portal BEGIN CREATE FOLDER
description: Create a portal navigation folder
---
EXECUTE portal BEGIN
  CREATE FOLDER '«FolderName»' UNDER '«ParentFolder»';
END;
