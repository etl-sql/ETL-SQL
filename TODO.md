
## Syntax Additions and Improvements

- **[x] FW-1. Fixed-Width CSV Support**
    - **Draft Syntax**: `CREATE CONNECTION c ON FLATFILE WITH(FORMAT='FIXED', TEMPLATE=#temp);`
    - **Mechanism**: Use the `#temp` table schema as the layout template. Field widths are extracted from standard lengths (e.g. `VARCHAR(20)`) or custom tags `/* @width: 20 */`.
    - **Key Options**: Support `TRIM=ON|OFF` for automatic whitespace removal and `SKIP_HEADER=N` to handle metadata rows.
    - **Gotchas**: Ensure the template offsets account for varied line endings (`\r\n` vs `\n`).

- **[x] FW-2. Add overwrite option for copy's and moves**
   - `COPY_FILE(<source>, <destination>, [OVERWRITE=ON|OFF]);`
   - `MOVE_FILE(<source>, <destination>, [OVERWRITE=ON|OFF]);`
   - `COPY_DIRECTORY(<source>, <destination>, [OVERWRITE=ON|OFF]);`
   - `MOVE_DIRECTORY(<source>, <destination>, [OVERWRITE=ON|OFF]);`
   Add new functions that are equivalent to the above but with different names.
   - `COPY FILE '<source>' TO '<destination>' [WITH(OVERWRITE=ON|OFF)];` -- equivalent to COPY_FILE
   - `MOVE FILE '<source>' TO '<destination>' [WITH(OVERWRITE=ON|OFF)];` -- equivalent to MOVE_FILE
   - `RENAME FILE '<source>' TO '<destination>' [WITH(OVERWRITE=ON|OFF)];` -- equivalent to RENAME_FILE
   - `DELETE FILE '<source>';` -- equivalent to DELETE_FILE
   - `COMPRESS FILE '<source>' TO '<destination>' [WITH(OVERWRITE=ON|OFF)];` -- equivalent to COMPRESS_FILE
   - `ENCRYPT FILE '<source>' TO '<destination>' [WITH(OVERWRITE=ON|OFF)];` -- equivalent to ENCRYPT_FILE
   - `DECRYPT FILE '<source>' TO '<destination>' [WITH(OVERWRITE=ON|OFF)];` -- equivalent to DECRYPT_FILE
   - `CREATE DIRECTORY '<source>' [WITH(OVERWRITE=ON|OFF)];` -- equivalent to CREATE_DIRECTORY
   - `COPY DIRECTORY '<source>' TO '<destination>' [WITH(OVERWRITE=ON|OFF)];` -- equivalent to COPY_DIRECTORY
   - `MOVE DIRECTORY '<source>' TO '<destination>' [WITH(OVERWRITE=ON|OFF)];` -- equivalent to MOVE_DIRECTORY
   - `RENAME DIRECTORY '<source>' TO '<destination>' [WITH(OVERWRITE=ON|OFF)];` -- equivalent to RENAME_DIRECTORY
   - `DELETE DIRECTORY '<source>';` -- equivalent to DELETE_DIRECTORY
   - `COMPRESS DIRECTORY '<source>' TO '<destination>' [WITH(OVERWRITE=ON|OFF)];` -- equivalent to COMPRESS_DIRECTORY
   - `ENCRYPT DIRECTORY '<source>' TO '<destination>' [WITH(OVERWRITE=ON|OFF)];` -- equivalent to ENCRYPT_DIRECTORY
   - `DECRYPT DIRECTORY '<source>' TO '<destination>' [WITH(OVERWRITE=ON|OFF)];` -- equivalent to DECRYPT_DIRECTORY
   - `DELETE DIRECTORY_CONTENTS '<source>' [WITH(RECURSIVE=ON|OFF)];` -- equivalent to DELETE_DIRECTORY_CONTENTS


- **[x] FW-3. Redo remote sends and email to match other functions**
   - Change SEND_FILE to SEND_FILE('C:\Exports\report.csv', my_sftp, '/uploads/report.csv', [OVERWRITE=ON|OFF]);
   - Add equivalent to SEND_FILE as SEND FILE '<local_path>' TO '<remote_path>' AT <connection_name> [WITH(OVERWRITE=ON|OFF)];
   
   - Change RECEIVE_FILE to RECEIVE_FILE(my_sftp, '/data/input.csv', 'C:\Imports\input.csv', [OVERWRITE=ON|OFF]);
   - Add equivalent to RECEIVE_FILE as RECEIVE FILE FROM '<remote_path>' TO '<local_path>' AT <connection_name> [WITH(OVERWRITE=ON|OFF)];

    - change SEND_EMAIL to SEND_EMAIL(<smtp_connection>, '<to_address>', '<from_address>', '<subject>', '<body>', ['<cc_address>, ...'], ['<bcc_address>, ...'], ['<file_path>, ...']);
    - Add equivalent to SEND_EMAIL as 
*Syntax:*
```sql
SEND EMAIL TO '<to_address>'
FROM '<from_address>'
SUBJECT '<subject>'
BODY '<body>'
[CC '<cc_address>' [, '<cc2>', ...]]
[BCC '<bcc_address>' [, '<bcc2>', ...]]
[ATTACH '<file_path>' [, '<file2>', ...]]
AT <smtp_connection>;
```

  **[x] FW-4. Add the equivalents to the help for the function.**
  example COPY_FILE add this to the help menu VERBOSE: 
  COPY FILE '<source>' TO '<destination>' [WITH(OVERWRITE=ON|OFF)];
  And for COPY FILE '<source>' TO '<destination>' [WITH(OVERWRITE=ON|OFF)] help menu add SHORTHAND: COPY_FILE('<source>', '<destination>', [OVERWRITE=ON|OFF]);

  Can you do this for all the functions above with the SQL syntax as VERBOSE and function style as SHORTHAND.

  This way the users can see that both do the same thing and the help menu reflects that.  Also if we haven't already all the functions should have help menus showing them the options.  Also they should all be listed in the ETL_SQL_LANGUAGE_REFERENCE.md file.

  **[x] FW-5. Add SYSDATE to the language**
  - Add SYSDATE to the language
  - Add SYSDATE to the ETL_SQL_LANGUAGE_REFERENCE.md file

  **[x] FW-6. Add date add/subtract shorthand**
  - GETDATE() + 1 should add 1 day, GETDATE() - 1 should subtract 1 day.
  - Same behavior for SYSDATE, CURRENT_TIMESTAMP, NOW(), etc.

  **FW-7. Add ORDER BY <number>**
  -- Add ORDER BY <number> to the ORDER BY clause, the number corresponds to the column number in the SELECT clause. (1 based)
  -- Make sure ASC | DESC is supported.
  -- Add examples to the ETL_SQL_LANGUAGE_REFERENCE.md file for ORDER BY <number>.
  -- Add example to the ETL_SQL_LANGUAGE_REFERENCE.md file for ORDER BY column_name with ASC | DESC.

  **FW-8. Add PERCENT and WITH TIES to TOP**
  -- Add TOP (expression) [ PERCENT ] [ WITH TIES ] 
Explanation:
PERCENT
Indicates that the query returns only the first expression percent of rows from the result set. If the calculated number of rows is a fraction, it's rounded up to the next whole number.

WITH TIES
Returns two or more rows that tie for last place in the limited results set. You must use this argument with the ORDER BY clause. WITH TIES might cause more rows to be returned than the value specified in expression. For example, if expression is set to 5 but two more rows match the values of the ORDER BY columns in row 5, the result set contains seven rows.

You can specify the TOP clause with the WITH TIES argument only in SELECT statements, and only if you also specify the ORDER BY clause. The returned order of tying records is arbitrary. ORDER BY doesn't affect this rule.

  **FW-9. Add advanced TRIM function**
  -- TRIM ( [ LEADING | TRAILING | BOTH ] [characters FROM ] string )

  **FW-10. Add advanced SUBSTRING function**
  -- SUBSTRING ( string FROM start [ FOR length ] )

  **FW-11. Position function**
  -- POSITION ( substring IN string )

  **FW-12. OVERLAY function**
  -- OVERLAY ( string PLACING overlay_string FROM start [ FOR length ] )

  **FW-13. EXTRACT function**
  -- EXTRACT ( field FROM source )

  **FW-14. OCTET_LENGTH function**
  -- OCTET_LENGTH ( string )

  **FW-15. CHARACTER_LENGTH function**
  -- CHARACTER_LENGTH ( string )

  **FW-16. CHAR_LENGTH function**
  -- CHAR_LENGTH ( string )

  **FW.17. Add advanced statistical functions**
  -- STDDEV_POP / STDDEV_SAMP: Population and sample standard deviation.
  -- VAR_POP / VAR_SAMP: Population and sample variance.CORR(y, x): Computes the correlation coefficient between two sets of numbers.
  -- COVAR_POP(y, x) / COVAR_SAMP(y, x): Computes population and sample covariance.
  -- REGR_SLOPE / REGR_INTERCEPT: Linear regression slope and y-intercept for a set of pairs.
  -- EVERY / ANY / SOME: Aggregates for boolean values (returns true if all or some values are true).

  **FW-18. Examples in the documentation on AT TIME ZONE**
  - Add examples to the ETL_SQL_LANGUAGE_REFERENCE.md file for AT TIME ZONE showing the different time zones and how they work.
  - Show an example query with AT TIME ZONE that converts a UTC timestamp to a local time zone.
  - Be sure to list the valid time zones in the documentation.

## VS CODE Bugs/Improvements


