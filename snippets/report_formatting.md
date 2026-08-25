---
trigger: $report_formatting
label: SET REPORT TIME_ZONE / LOCALE / NULL_LABEL
description: Deterministic report formatting — the zone, culture, and NULL text every renderer uses
---
SET REPORT TIME_ZONE = '«America/New_York»';
SET REPORT LOCALE = '«en-US»';
SET REPORT NULL_LABEL = '«-»';
