# Autocomplete Snippets

This folder is the source of truth for built-in ETL-SQL autocomplete snippets.

The files are Markdown because the snippet loader reads YAML frontmatter plus a Markdown body, but they are not documentation pages. During build, `src/ETL-SQL.Core/ETL-SQL.Core.csproj` embeds `snippets/*.md` as `Resources/Help/Snippets/*` so the engine, TUI, and language server can load the same templates.

## Runtime Flow

1. A snippet is authored in this folder.
2. `ETL-SQL.Core` embeds the file as a resource under `Resources/Help/Snippets`.
3. `SnippetLibrary` loads the embedded snippets at startup.
4. The TUI, VS Code language server, and `HELP SNIPPETS` surface the template.
5. User snippets from `Snippets:UserSnippetsPath` are loaded alongside built-ins and can override a built-in trigger.

Do not edit generated or embedded resource copies directly. Update the file in this folder instead.

## File Format

Every snippet file must start with YAML frontmatter:

```yaml
---
trigger: $dataset
label: CREATE DATASET &name
description: Shared, optionally cached report dataset referenced by multiple visuals
---
```

Required fields:

- **trigger** - Autocomplete trigger. It must start with `$`.
- **label** - Short display label shown in completion lists.
- **description** - Brief help text shown with the completion item.

The body after the frontmatter is inserted when the snippet is accepted.

```sql
CREATE DATASET &«name» REFRESH EVERY '«1h»' AS (
  SELECT «col1», «col2»
  FROM «source»
  WHERE «condition»
);
```

## Placeholders

Use French quotation marks around placeholder names:

```sql
SELECT «ColumnName»
FROM «TableName»;
```

The editor uses `«` and `»` to identify tab-stop placeholders. Keep placeholder names short and descriptive.

## Authoring Rules

- Use two-space indentation inside SQL blocks.
- Keep snippets valid enough to parse after placeholders are replaced.
- Keep triggers lowercase unless the snippet represents an established uppercase token.
- Do not include secrets, real connection strings, or environment-specific paths.
- Keep examples portable and script-first.

## Related Documentation

- [Snippet Reference](../docs/reference/snippets/README.md)
- [Help and Snippet Standards](../docs/architecture/standards/Help_and_Snippet_Standards.md)
