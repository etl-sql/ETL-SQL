# ETL-SQL Development Roadmap
## Bugs
### VS Code
 -[x] When loading a new query the results frame doesn't clear like it should.  This was working in the past.  It should clear whenever a new query window, or a script is opened.
 -[x] Expanding without an alias shows a fully qualified name for each column.  This wouldn't be an issue if it worked but it instead returns NULL.
    Either make the fully qualified name work or remove the m.FILE.  This needs to be tested it has broken multiple times.
```sql
 CREATE CONNECTION m ON FLATFILE('"C:\Users\chuck\scratch\ETL-SQL\TestData\test_categories.csv"');
 SELECT m.FILE.id, m.FILE.category_name FROM m.FILE;
```
-[ ] Getting an intermittent ETL-SQL Error: REPL process exited unexpectedly issue when first executing, after the first one it seems fine.
-[ ] Execution tree has arrows to allow you to scroll back to see what previously happened, first can that move to the bottom, second it needs to be session specific it showed all executions even though 1/2 came from another script.

### Reporting
 Using "C:\Users\chuck\scratch\ETL-SQL\samples\10_Kitchen_Sinks\report_kitchen_sink.rptsql"
 -[x] Revenue Sunburst chart is blank
 -[ ] Clicking AdvancedCharts does not make the button turn the selected Blue
 -[x] Revenue by Category -- Faceted by Region has all the pieces just nothing is showing for data.
 -[x] Cross highlight works great on Revenue by Region to Category Breakdown but the opposite doesn't work.  Clicking on Category Breakdown should cross highlight Revenue by Region but instead the entire Revenue by Region chart is ghosted.

## Up Next
