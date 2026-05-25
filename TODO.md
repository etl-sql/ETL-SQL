# ETL-SQL Development

## Future Pipeline Goals

- [ ] **[Engine] Pipeline Checkpoints / State Resume**
  - Detail: Implement native checkpoint management using T-SQL style section labels (`LabelName:`) as implicit checkpoint markers.
  - Features to add:
    - **Labels**: Lex and parse `LabelName:` as a `SectionLabelStatement`.
    - **GOTO**: Add keyword and parse `GOTO LabelName;` as control-flow statement.
    - **Checkpoint Serialization**: Auto-serialize `#temp` tables (via Arrow spill) and variable scope (via JSON) when hitting a top-level label.
  - Scoping & Guardrails:
    - Only top-level labels trigger state checkpointing (nested labels are GOTO-only targets).
    - Allow jumping OUT of nested loops, conditionals, and `TRY...CATCH` blocks.
    - Block (raise compiler error) jumping INTO nested loops, conditionals, and `TRY...CATCH` blocks.
    - Prevent cross-script file jumps.
    - LSP Integration: Expose labels in outlines (for folding and jumping) and enable autocomplete for `GOTO`.
    - **Documentation**:
      - Update [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) to document label/GOTO syntax and scoping constraints.
      - Update [User_Manual.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/User_Manual.md) to walk through the state-resume pipeline workflow.
      - Update [Specialized_Operations.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Specialized_Operations.md) with details of the `--resume` CLI parameters.

- [ ] **[Connectors] First-Class Native MySQL/MariaDB Connector**
  - Detail: Introduce a native `MySqlConnector` provider client registration to eliminate ODBC bridge dependency and improve native dialect parsing and exception-wrapping for MySQL and MariaDB servers.
