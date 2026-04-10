# ETL-SQL Development Roadmap
## VS Code Extension Bugs
- [ ] Messages: Ensure connection lifecycle and row-count telemetry (Dropped, Created, Row Counts) appear as distinct lines in the Messages tab.

## Upcoming REPL Enhancements
- [ ] **Variable Explorer**: Add to the sidebar tab to inspect engine variables (`@var`) and session state after a script run.
- [ ] **Data Export**: Add 'Export to CSV' and 'Export to Excel' buttons to the Results panel toolbar.
- [ ] **Session Verification**: Regression test 'SET SESSION_PERSISTENCE = ON' to confirm state sharing works across independent REPL executions.

## Terminal IDE Architecture (Next Phase)
- [ ] **UI Framework**: Initialize SharpConsoleUI boilerplate and create `MainView` (Window).
- [ ] **Layout Engine**: Implement 3-Row, 2-Column Grid/Dock layout.
- [ ] **Component Bridge**: Wrap existing Spectre Tree and Table methods into `SpectreRenderableControl`.
- [ ] **Editor Logic**: Integrate `MultilineEdit` with Lexer/Parser for real-time validation.
- [ ] **Execution Hook**: Bind 'Run' event to update UI panes asynchronously.