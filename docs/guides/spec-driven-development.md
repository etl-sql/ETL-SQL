# Specification-Driven Pipeline Development

This guide describes how to use specification files (PDFs, Excel workbooks, Word documents, or CSVs) to generate structured ETL-SQL starter scripts.

By leveraging an AI to parse unstructured documents into a standard JSON schema model, and compiling that model with the `etl-sql gen-script` command, you can save the transcription and boilerplate time in pipeline creation while keeping schema validation, governance tagging, validation summaries, and quarantine scaffolding in the generated output. This workflow still expects a developer to review the JSON, complete the source extraction query, and test with real vendor data.

There are two paths through this workflow: the **VS Code extension path** (automated, recommended) and the **manual CLI path**. Both paths produce identical output because the VS Code extension drives `gen-script` internally.

---

## The Workflow

```
┌─────────────────────────┐
│ 1. Data Spec File       │ (Unstructured: PDF/Excel/CSV/Word)
└────────────┬────────────┘
             │
             │  PATH A — VS Code Extension           PATH B — Manual CLI
             │  Right-click → Generate Script   OR   Paste prompt + file into LLM
             ▼                                        ▼
┌─────────────────────────┐               ┌─────────────────────────┐
│ 2. Spec JSON Contract   │               │ 2. Spec JSON Contract   │
│  (generated & saved     │               │  (manually saved as     │
│   by extension)         │               │   my_spec.json)         │
└────────────┬────────────┘               └────────────┬────────────┘
             │                                         │
             │  Extension runs gen-script              │  etl-sql gen-script
             ▼                                         ▼
┌──────────────────────────────────────────────────────────────────┐
│ 3. ETL-SQL Template  (casting, validation, lineage tags)         │
└────────────┬─────────────────────────────────────────────────────┘
             │
             │ Developer writes extraction query into #staging
             ▼
┌─────────────────────────┐
│ 4. Executable Pipeline  │ (Deterministic, schema-safe execution)
└─────────────────────────┘
```

---

## Path A — VS Code Extension (Recommended)

The ETL-SQL VS Code extension integrates the entire spec-to-script workflow into the Explorer context menu. You do not need to manually call any AI or save intermediate JSON files — the extension handles all of that automatically.

### A.1 Configure Your AI Provider

Before running the command for the first time, configure your preferred AI provider in VS Code settings (`File → Preferences → Settings`, search for `etlsql.ai`):

| Setting | Key | Description |
| :--- | :--- | :--- |
| **Provider** | `etlsql.ai.provider` | AI service to use for spec parsing. Default: `Gemini`. |
| **API Key** | `etlsql.ai.apiKey` | Your API key for the chosen provider. Not required when using VS Code Chat Extensions. |
| **Model** | `etlsql.ai.model` | Optional model override (e.g. `gemini-1.5-flash`, `gpt-4o`, `claude-3-5-sonnet-latest`). Leave blank to use the provider default. |
| **Endpoint** | `etlsql.ai.endpoint` | Advanced: override the default API endpoint URL. Required only for `Custom` providers (e.g. local Ollama, LocalAI, or private gateways). |

**Supported providers and file-type compatibility:**

| Provider | PDF | Excel/Word | CSV / JSON / TXT |
| :--- | :---: | :---: | :---: |
| Gemini | ✓ | ✓ | ✓ |
| Anthropic | ✓ (PDF only) | ✗ | ✓ |
| OpenAI | ✗ | ✗ | ✓ |
| OpenRouter | ✗ | ✗ | ✓ |
| VS Code Chat Extensions (Copilot/Claude/etc.) | ✗ | ✗ | ✓ |
| Custom | ✗ | ✗ | ✓ |

> [!TIP]
> Use **Gemini** or **Anthropic** when your vendor spec is a PDF or Excel workbook. Use **VS Code Chat Extensions** for text-based specs if you want to avoid managing an API key — just ensure GitHub Copilot (or another chat extension) is installed and enabled.

### A.2 Right-Click the Spec File

1. In the VS Code **Explorer**, locate your specification file.
2. Right-click the file and select **ETL-SQL: Generate Script from Spec**.

The command accepts: `.pdf`, `.xlsx`, `.xls`, `.docx`, `.doc`, `.csv`, `.tsv`, `.json`, `.txt`.

> [!NOTE]
> The command is only shown in the context menu for files with a supported extension. It is also available via the VS Code Command Palette (`Ctrl+Shift+P` → `ETL-SQL: Generate Script from Spec`), which opens a file picker so you can select the spec manually.

### A.3 Trim Large PDFs (Optional Prompt)

If you selected a PDF, the extension asks:

> *Would you like to trim this PDF first using `extract-spec` to isolate data dictionary pages and reduce LLM token usage?*

Choose **Yes (Recommended)** for large vendor PDFs (50+ pages). The extension runs `extract-spec` in the background using heuristic analysis (scanning for database type keywords and column header terms) to isolate the data dictionary pages before sending them to the AI. The trimmed file is written to a temp directory automatically — no manual steps needed.

### A.4 AI Extraction and JSON Compilation

The extension:

1. Sends the spec file (or trimmed PDF) to the configured AI provider along with the bundled `data_spec_parser_instructions.md` prompt.
2. Receives the structured JSON contract back from the AI.
3. Validates that the JSON parses correctly.
4. Prompts you to **choose a save location** for the output script (defaulting to your workspace root with a name derived from `pipeline_name` in the JSON).
5. Runs `gen-script` internally against the JSON to produce the final `.etlsql` file.
6. Opens the generated script in the editor automatically.

If the JSON produced by the AI is invalid, or if `gen-script` finds schema contract violations (missing required fields, bad enum values, duplicate column names, malformed `validation_regex`, etc.), the extension surfaces the error in a VS Code error notification.

### A.5 Complete the Extraction Query

Once the script is open in the editor, proceed to [Step 3: Complete the Extraction Query](#step-3-complete-the-extraction-query) below — the instructions are identical regardless of which path you used.

---

## Path B — Manual CLI

Use this path if you prefer to control each step directly, work outside VS Code, or need to script the generation process in a pipeline.

### Step 0: Extract Schema from Large PDFs (Optional)

If you are dealing with a large vendor PDF specification (e.g., 50+ pages containing API credentials, security whitelists, and introduction fluff), you should trim the PDF first. This ensures the LLM's context window focuses exclusively on the data dictionary tables, preventing token limits and extraction drift.

Run the `extract-spec` command:

```powershell
etl-sql extract-spec --input ./Specs/large_vendor_spec.pdf --output ./Specs/trimmed_spec.pdf
```

The C# extraction engine uses heuristic analysis (scanning for database type keywords and column header terms while penalizing API/connectivity jargon) to isolate likely schema pages and output them to the trimmed PDF.

### Step 1: Generate the Intermediate JSON

1. Copy the contents of the prompt instructions in [data_spec_parser_instructions.md](file:///C:/Users/chuck/scratch/ETL-SQL/src/etl-sql-vscode/resources/data_spec_parser_instructions.md).
2. Paste the prompt text into your AI assistant (e.g. Gemini, ChatGPT, or Claude) and upload the specification document (use the `trimmed_spec.pdf` if you ran Step 0).
3. Save the resulting JSON block in your local repository as `my_spec.json`.
4. (Optional) Review the JSON file. If the AI misunderstood any columns or custom types, edit them directly in the JSON file.

### Step 2: Compile the JSON to ETL-SQL Boilerplate

Run the CLI command `gen-script` to compile the JSON schema definition:

```powershell
etl-sql gen-script --schema ./my_spec.json --output ./Scripts/load_feed.etlsql
```

Before writing any `.etlsql` file, `gen-script` validates the JSON against the specification contract. The machine-readable contract is checked in at [spec_pipeline.schema.json](file:///C:/Users/chuck/scratch/ETL-SQL/docs/reference/configuration/spec_pipeline.schema.json). If the AI output is missing required fields, mixes root-level `schema` with `datasets`, has unsupported enum values, duplicate column names, invalid numeric bounds, or a malformed `validation_regex`, the command prints the validation errors and stops.

The command generates a pre-formatted ETL-SQL script containing:
*   Header blocks with metadata descriptions, owner information, and security classifications.
*   AI extraction review notes for confidence scores and source evidence when the JSON includes them.
*   Source layout notes for file format, headers, skipped rows, null tokens, keys, duplicate policy, fixed-width positions, date formats, and allowed values when the vendor spec provides them.
*   Outbound connection declarations (e.g. `CREATE CONNECTION ... AS FLATFILE`) mapped from the spec.
*   Cleansing and casting statements (e.g. `TRY_CAST`, `SUBSTRING`) for every target column.
*   Validation review tables for regex and allowed-value checks, with optional quarantine behavior when `source.reject_policy` is `quarantine`.
*   Lineage tagging declarations using `TAG` (see [Lineage.md](file:///C:/Users/chuck/scratch/ETL-SQL/docs/reference/statements/session-control/lineage.md)).
*   An `EXPECT SCHEMA` constraint validator (see [EXPECT SCHEMA](../reference/statements/ddl/expect-schema.md)).

---

## Step 3: Complete the Extraction Query

Open the compiled script (e.g. `./Scripts/load_feed.etlsql`). The script contains a placeholder area:

```sql
-- =========================================================================
-- EXTRACT PHASE (USER CONTRACT)
-- =========================================================================
-- [USER TODO]: Define your source connection and query into #staging below.
-- All columns in #staging must match the schema names defined in EXPECT SCHEMA.
```

Write your source connection and extraction logic inside the block. For example:

```sql
CREATE CONNECTION src_db AS POSTGRES(HOST='dw-host', DATABASE='sales', USER='...', PASSWORD='...');

SELECT 
    c.id       AS customer_id,
    c.name     AS customer_name,
    c.email    AS email,
    c.active   AS active_flag
INTO #staging
FROM src_db.public.customers c;
```

If the JSON includes `confidence` or `source_evidence`, the generated placeholder includes AI extraction review notes. Treat low-confidence fields and evidence comments as a review checklist before running the pipeline. If the JSON includes `source` metadata, the generated placeholder also includes the inferred source connection and comments for the vendor layout. Confirm header rows, skipped rows, null tokens, fixed-width positions, date formats, allowed values, and duplicate rules before running the pipeline.

For a complete worked example, see [Cookbook recipe 25](file:///C:/Users/chuck/scratch/ETL-SQL/docs/cookbooks/etl-recipes.md) and the runnable sample [realworld_12_spec_driven_customer_feed.etlsql](file:///C:/Users/chuck/scratch/ETL-SQL/samples/07_Real_World/realworld_12_spec_driven_customer_feed.etlsql).

---

## Validation Gates

At runtime, the script executes safety assertions before uploading data:

1.  **Type & Alignment Gate:** The `EXPECT SCHEMA` check ensures the extraction query output `#staging` is structured exactly as the target spec expects, preventing unexpected upstream shifts from crashing database inserts.
2.  **Length & Format Check:** The cleansing query automatically truncates long strings using `SUBSTRING` and records regex or allowed-value failures in `#spec_validation_issues`.
3.  **Reject Handling:** With `source.reject_policy = "quarantine"`, invalid rows are written to `#rejected_data` and only `#valid_data` is uploaded. With `fail_batch`, the script throws after writing validation counts. With `warn`, it prints a warning and continues.
4.  **Governance Tagging:** The derived columns automatically inherit classification properties like `@pii` or `@confidential` and push them downstream to trace data lineage.


