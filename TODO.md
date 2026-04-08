
## Syntax Additions and Improvements
- [x] W-1. Add WAITFOR TIME
  -- Add WAITFOR TIME to the language
  -- Add WAITFOR TIME to the ETL_SQL_LANGUAGE_REFERENCE.md file
  -- Add examples to the ETL_SQL_LANGUAGE_REFERENCE.md file for WAITFOR TIME.

- [x] W-2. Cast to all types
  -- Check to make sure CAST/TRY_CAST works to cast to all available types.
  -- Add examples to the ETL_SQL_LANGUAGE_REFERENCE.md file for CAST to all available types.

- [x] W-3. Missing from documentation
  -- ASIN, ACOS, ATAN, ATAN2, SIGN seem to be missing from ETL_SQL_LANGUAGE_REFERENCE.md file.

- [x] W-4.Fix syntax for SSH_KEY_PAIR
  -- CREATE_SSH_KEY_PAIR('<directory_path>' [, <bits>, '<algorithm>', '<passphrase>', '<comment>']);  This is the function style syntax.
  -- Add SQL style syntax for SSH_KEY_PAIR.
  CREATE SSH_KEY_PAIR '<directory_path>' [WITH([BITS=<bits>][, ALGORITHM='<algorithm>'][, PASSPHRASE='<passphrase>'][, COMMENT='<comment>'])];
  -- Add examples to the ETL_SQL_LANGUAGE_REFERENCE.md file for SSH_KEY_PAIR.
  -- Add help text for SSH_KEY_PAIR.

- [x] W-5. Fix Docker function style syntax
  Need to add the () around the alias.
  -- START_DOCKER(<alias>) - Starts a Docker container for the specified connection.
  -- STOP_DOCKER(<alias>) - Stops a running Docker container.
  -- PAUSE_DOCKER(<alias>) - Pauses a running Docker container.
  -- CLOSE_DOCKER(<alias>) - Stops and removes a Docker container.
  -- Add examples to the ETL_SQL_LANGUAGE_REFERENCE.md file for Docker operations.
  -- Add help text for Docker operations.

- [x] W-6. Help in documentation
Help current just shows: HELP CONNECTION <type>.  I think we made this much more broad than just connections.
  -- Add help text for all functions in the ETL_SQL_LANGUAGE_REFERENCE.md file.

- [x] W-7. PRINT command
  -- The PRINT() function is shown in the ETL_SQL_LANGUAGE_REFERENCE.md file but I don't see PRINT '<message>' in the syntax.
  -- Make sure PRINT '<message>' is implemented.
  -- Add PRINT '<message>' to the ETL_SQL_LANGUAGE_REFERENCE.md file.
  -- Add examples to the ETL_SQL_LANGUAGE_REFERENCE.md file for PRINT '<message>'.

- [x] W-8. Tag syntax
  Need to add SQL style syntax for tags.
  -- 'GET_TAGS(table_name [, column_name])' can be set to a list but you should also be able to do SELECT * FROM GET_TAGS(table_name [, column_name]);
  -- SHOW TAGS FOR TABLE <table_name> [COLUMN <column_name>]; -- This is the SQL style syntax for getting tags.
  -- 'GET_TAG_VALUE(table_name, column_name, tag_name)' The SQL style syntax for this is SHOW TAG VALUE FOR TABLE <table_name> [COLUMN <column_name>] WITH TAG <tag_name>;
  -- Add tag syntax to the ETL_SQL_LANGUAGE_REFERENCE.md file.
  -- Add examples to the ETL_SQL_LANGUAGE_REFERENCE.md file for tag syntax.

- [x] W-9. Add the ability to save the SHOW commands to a #temp table
 -- SHOW JOBS INTO #<temp_table_name>;
 -- SHOW JOB HISTORY INTO #<temp_table_name>;
 -- SHOW CONNECTIONS INTO #<temp_table_name>;
 -- SHOW TABLES INTO #<temp_table_name>;
 -- SHOW COLUMNS INTO #<temp_table_name>;
 -- SHOW PROFILE INTO #<temp_table_name>;
 -- SHOW TAGS FOR TABLE <table_name> [COLUMN <column_name>] INTO #<temp_table_name>;
 -- SHOW TAG VALUE FOR TABLE <table_name> [COLUMN <column_name>] WITH TAG <tag_name> INTO #<temp_table_name>;

## VS CODE Bugs/Improvements

## Stabilization Correctness
- [x] W-10. Fix File Transfer Syntax (optional commas and trailing semicolon)
- [x] W-11. Fix SEND_EMAIL Parsing (trailing semicolon and list handling)
- [x] W-13. Implement UNIQUE constraints in DataTable.AddRow
- [x] W-14. Resolve Session Persistence data loss (persist full schema and constraints)
