# Snippets

Snippets are reusable autocomplete templates for ETL-SQL scripts and Report-SQL reports. They appear in the TUI, VS Code language server completions, and `HELP SNIPPETS`.

## Built-In Snippets

Built-in snippets live in the repository root:

- [`/snippets`](../../../snippets/README.md)

They are product resources rather than normal documentation pages. The build embeds `snippets/*.md` into `ETL-SQL.Core` under `Resources/Help/Snippets`, where the runtime snippet library loads them.

## User Snippets

Teams can add their own snippets by setting `Snippets:UserSnippetsPath` in configuration:

```json
{
  "Snippets": {
    "UserSnippetsPath": "C:\\SharedConfig\\etlsql-snippets"
  }
}
```

Each file in that directory uses the same Markdown format as built-ins: YAML frontmatter followed by the snippet body.

```markdown
---
trigger: $mysnippet
label: My Custom Snippet
description: Inserts a team-standard query pattern
---
SELECT «ColumnName»
FROM «TableName»;
```

User snippets with the same trigger as a built-in override the built-in version. Restart the application after changing the configured snippet directory.

## Frontmatter

- **trigger** - Completion trigger. It must start with `$`.
- **label** - Short title shown in completion lists.
- **description** - One-line explanation shown with the completion.

## Placeholders

Placeholders use French quotation marks:

```sql
CREATE CONNECTION «ConnName» AS MSSQL(
  SERVER = '«server»',
  DATABASE = '«database»'
);
```

The editor cycles through `«placeholder»` fields when the snippet is accepted.

## Maintenance Notes

- Keep `/snippets` as the source of truth for built-ins.
- Do not edit generated resource copies under build output folders.
- Remove old physical `src/ETL-SQL.Core/Resources/Help` copies only after the project file embeds the replacement docs/snippets and build/test coverage confirms the runtime help library can load them.

## References

- [Autocomplete Snippets Source](../../../snippets/README.md)
- [Configuration Settings](../../administration/platform/appsettings-reference.md)
- [Help Statement](../statements/session-control/help.md)
- [Help and Snippet Standards](../../architecture/standards/Help_and_Snippet_Standards.md)
