# ETL-SQL Development Roadmap
## VS Code issues
- [ ] **On Error should go directly to the messages tab**  Currently just sits there and doesn't give you any indication of what happened until you start looking around and see a failure on the messages tab.
- [ ] **Paths surrounded in "" the "" should be ignored** When you paste a path in a connection string with "" surrounding it the "" should be ignored.  
- [ ] **Sometimes when executing there is a serious lag**  First why is it so slow to execute?  Second no visual indicator that its working.  Third the execute button should be disabled until its done running.

## Documentation

## Security Released (0.7.0)

## Up Next
- [ ] **Credential Auto-Decryption Expansion** Support auto-decryption of `ENC:` values in all connector options when assigned from `SENSITIVE` variables.

- [x] **Version 0.7.0: Arrow Columnar Format — Phase A (SpillStore IPC)**
    - Strategy document complete: `Docs/Strategy/Arrow_Columnar_Strategy.md`
    - **Phase A implemented:** `ArrowSpillWriter`/`ArrowSpillReader` replace JSON-line spill in `SpillStore.cs`.
    - `CREATE COLUMNAR TABLE` syntax and full `DataTable` replacement (Phase B/C) explicitly deferred.
    - **`Security:SpillFormat`** config key added — `"Arrow"` (default).
        
- [x] **Security Manifest**: Strategy document for script signing.
- [x] **Data Lake Connection brainstorm**: Strategy document complete.
---