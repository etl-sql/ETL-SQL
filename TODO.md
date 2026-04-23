# ETL-SQL Development Roadmap
## VS Code issues
- [ ] **On Error should go directly to the messages tab**  Currently just sits there and doesn't give you any indication of what happened until you start looking around and see a failure on the messages tab.
- [ ] **Showing multiple results broke**  This used to work there was an arrow left and right at the bottom to switch between results.  Now it just shows the last result.
- [ ] **When a new script is open the results, tree, messages, and performance tab should all be cleared**  Currently they are not.  
- [ ] **Hovering over a function should show the help for that function**  Currently it doesn't show anything.
- [ ] **Brainstorm if linting pushdown databases is possible**
- [ ] **Need a stop or cancel execution button**  Currently you can't stop a script once it starts executing.  You used to be able to do this but the button is gone.
- [ ] **Paths surrounded in "" the "" should be ignored** When you paste a path in a connection string with "" surrounding it the "" should be ignored.  
- [ ] **Sometimes when executing there is a serious lag**  First why is it so slow to execute?  Second no visual indicator that its working.  Third the execute button should be disabled until its done running.
- [x] **PARQUET was not being recognized by the suggestion engine**  I typed it all out but it never showed up as a suggestion. 
- [ ] **Results pane doesn't show NULL only result** Using this code:
```sql
DECLARE @id int;
SELECT @id;
```
Should return NULL.  Return blank

## Language issues
- [ ] **ENCRYPT_FILE and DECRYPT_FILE missing password parameter**
This is what it should be:
```sql
ENCRYPT_FILE('src', 'dest', 'password' [,OVERWRITE=ON|OFF])
DECRYPT_FILE('src', 'dest', 'password' [,OVERWRITE=ON|OFF])
```

-[x] **Variable already exists when running a second time**  I get the error: Variable @id has already been declared in this scope (Line 1, Col 9).  I like that it holds onto values so you can run the script a piece at a time.  For most objects we have the DROP IF EXISTS command.  For variables if we run them a second time it should just overwrite the value.  

## Security

## Documentation

## Up Next
- [ ] **Add limit to GENERATE**  We should add a limit in appsettings.json to prevent someone from generating a massive table, default 10000 the admin can then modify if they choose.

- [ ] **Strategy Documentation for version 0.7.0: Arrow Columnar Format**
    - Do not implement we are going to write out a strategy document for this one.  It's a big step in the design of the system.
    - `DataTable` (row-oriented, boxed `object[]`) is the core temp-table representation — We'll do a hybrid approach.
    - `CREATE COLUMNAR TABLE #TempTable(...)` will create a columnar table.
    - **Benefits Identified:**
        - **10–50x performance improvement** via SIMD/vectorized processing of columns.
        - **Memory density:** Avoids overhead of boxed objects; stores primitives in contiguous memory arrays.
        - **Zero-copy interoperability:** Enables high-speed handoff to Python/R/C++ analytical libraries.
        - **Native Spilling:** Arrow IPC format is a standard-compliant alternative for Strategy 2.3 spilling.
    - **Implementation Impact:**
        - Requires refactoring nearly every logic handler (Aggregate, Join, Sort) to use vectorized kernels instead of LINQ-over-Rows.
        - Prerequisite: Streaming (2.1) and Spilling (2.3) should be completed first.
        - **Hybrid Approach:** `CREATE COLUMNAR TABLE #TempTable(...)` allows both worlds to work without having to completely rewrite the engine.
        
- [ ] **Security Manifest**: Implement script signing for cryptographically verified execution.
  - Do not implement we will write out a strategy document for this one as well.
  - Need more, we'll need to brainstorm this one.  I don't want to put too much in the way of the user so we'll want to think this one through.

- [ ] **Data Lake Connection brainstorm**  How do we effectively handle data lake connections?  Do we need a separate connector for each type of data lake?  Do we need a way to handle authentication?  Do we need a way to handle schema evolution?  Lets do a strategy document for this one. 
---