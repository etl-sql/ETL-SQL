---
trigger: $portal_group
label: EXECUTE portal BEGIN CREATE GROUP / ADD USER
description: Create a portal security group and add a member
---
EXECUTE portal BEGIN
  CREATE GROUP '«GroupName»';
  ADD USER '«username»' TO GROUP '«GroupName»';
END;
