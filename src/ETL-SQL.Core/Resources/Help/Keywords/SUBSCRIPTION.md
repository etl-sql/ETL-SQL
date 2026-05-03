SUBSCRIPTION — Portal subscription management statements (requires a REPORTPORTAL connection).

CREATE SUBSCRIPTION [<name>]
  FOR REPORT '<script-path>'
  DELIVER TO '<email>'
  SCHEDULE '<cron>'
  FORMAT PDF|CSV|BOTH|LINK
  AT <smtp-alias>
  [ PARAMETERS (@param = 'value', ...) ];

ALTER SUBSCRIPTION <name-or-id>
  [ SET SCHEDULE '<cron>' ]
  [ SET FORMAT PDF|CSV|BOTH|LINK ]
  [ SET ACTIVE | INACTIVE ]
  [ PARAMETERS (@param = 'value', ...) ];   -- replaces full set; PARAMETERS() clears all

DROP SUBSCRIPTION <name-or-id>;

SHOW SUBSCRIPTIONS [FOR REPORT '<path>'] [INTO #temp];

RELDATE parameter values (resolved fresh on each delivery):
  'D-1'   Yesterday    'W-1'  Start of last week
  'M-1'   Start of last month   'ME-1'  End of last month
  'Y-1'   Jan 1 of last year    'N-2H'  2 hours ago

Example:
  CREATE SUBSCRIPTION DailySales
    FOR REPORT '/Reports/Sales/Daily'
    DELIVER TO 'team@example.com'
    SCHEDULE '0 7 * * MON-FRI'
    FORMAT PDF
    AT corporate-smtp
    PARAMETERS (@start = 'D-1', @end = 'D');
