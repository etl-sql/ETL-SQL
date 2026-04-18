# ETL-SQL Development Roadmap

## Up Next
- [ ] **Code check** a lot of changes have been made what can we improve?
  - [ ] Does this method comply with single responsibility?
  - [ ] Are there security issues?
  - [ ] Are there performance issues?
  - [ ] Has this been documented?
  - [ ] **Does this need a linter rule?** (Added SpillSecurityRule)
  - [ ] Can this be written better, simpler?
  - [ ] Is this being tested?
  Is this included in the \Architecture\ documentation?
  Are there edge cases we haven't considered?

- [ ] **Remove @@dataSet** This does nothing that a temp table doesn't already do, confusing to have it.  Remove from any documentation.  Check all *.md documents for the use of @@dataset and replace it with #temp tables.

- [ ] **CREATE STYLE**  I think we missed the word CREATE when making a STYLE so we need to add that to the syntax it should be CREATE STYLE <name> (<options>).  This is wrong in the Report_SQL_Guide.md

- [ ] **ALTER CHART, PAGE, CONTAINER, STYLE, NAVIGATION, DATASET**  Just like ALTER works in a query you can ALTER the items above.  Using ALTER only changes what is changed in the ALTER statement.

- [ ] **CREATE OR ALTER CHART, PAGE, CONTAINER, STYLE, NAVIGATION, DATASET**  Just like CREATE OR ALTER in a query but in this case it CREATES if it doesn't exists or ALTERS it if it does but in this case ALTER recreates it and does not use any existing options or settings.

- [ ] **Need STYLE samples** We need some CREATE STYLE samples in the Report_SQL_Guide.md and the Report_Cookbook.md.

- [ ] **CREATE BUTTON** We need buttons in reports.  CREATE BUTTON <name> AS <button type> (<options>).  This will have an ACTION option.  Will also need the ALTER, CREATE OR ALTER, and DROP commands for this.  Possible button types: BACK, REFRESH, HELP, ...

- [ ] **Need a TOOLTIP option in all report objects**  TOOLTIP = '<string>' or TOOLTIP (<container object with charts> or <markdown>)

- [ ] **Need DROP CHART, PAGE, CONTAINER, STYLE, NAVIGATION, DATASET** This removes the object.  With our ACTION, likely from a button this could remove these objects. 

- [ ] **Create our own style templates**  Need a way to create our own style templates that can be reused.  These will have to save as a file that can be imported.  Thinking we'll need a custom template folder.  When checking for templates the code will look at the Echart ones and the ones in the custom template folder.  CREATE TEMPLATE <name> ( <options>);  Need a way to ALTER, CREATE OR ALTER, and DROP to remove.