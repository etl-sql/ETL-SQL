---
trigger: $tag_report
label: Report metadata and governance tags
description: Report title, description, and script-level stewardship metadata for .rptsql files
---
/*
@owner: «team_or_person»
@steward: «data_steward»
@contact: «owner@example.com»
@domain: «Finance»
@classification: «internal»
@quality: «silver»
@d: «Report purpose and governed audience»
*/

SET REPORT TITLE = '«Report Title»';
SET REPORT DESCRIPTION = '«Short report description»';
