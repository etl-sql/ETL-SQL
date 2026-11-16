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

### A.1 Have a VS Code Chat Model

The command sends the spec to a chat model from a VS Code chat extension you have installed and
signed in to, such as GitHub Copilot. That extension decides which service receives the spec.
ETL-SQL stores no API key and calls no AI service itself. VS Code asks once for permission to let
ETL-SQL use the model.

To pick a model, set `etlsql.ai.model` to a model family (for example `gpt-4o` or
`claude-sonnet-4`). Leave it blank to use the first available model.

Chat models read text only. The command accepts `.csv`, `.tsv`, `.json`, `.xml`, `.txt` and `.md`
specs. For a PDF, Excel or Word spec, use [Path B](#path-b--manual-cli): trim the PDF with
`extract-spec` and give it to an AI assistant that reads documents.

### A.2 Right-Click the Spec File

1. In the VS Code **Explorer**, locate your specification file.
2. Right-click the file and select **ETL-SQL: Generate Script from Spec**.

> [!NOTE]
> The command is only shown in the context menu for files with a supported extension. It is also available via the VS Code Command Palette (`Ctrl+Shift+P` → `ETL-SQL: Generate Script from Spec`), which opens a file picker so you can select the spec manually.

### A.3 AI Extraction and JSON Compilation

The extension:

1. Sends the spec file to the chat model along with the bundled `data_spec_parser_instructions.md` prompt.
2. Receives the structured JSON contract back from the AI.
3. Validates that the JSON parses correctly.
4. Prompts you to **choose a save location** for the output script (defaulting to your workspace root with a name derived from `pipeline_name` in the JSON).
5. Runs `gen-script` internally against the JSON to produce the final `.etlsql` file.
6. Opens the generated script in the editor automatically.

If the JSON produced by the AI is invalid, or if `gen-script` finds schema contract violations (missing required fields, bad enum values, duplicate column names, malformed `validation_regex`, etc.), the extension surfaces the error in a VS Code error notification.

### A.4 Complete the Extraction Query

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

1. Copy the contents of the prompt instructions in [data_spec_parser_instructions.md](../../src/etl-sql-vscode/resources/data_spec_parser_instructions.md).
2. Paste the prompt text into your AI assistant (e.g. Gemini, ChatGPT, or Claude) and upload the specification document (use the `trimmed_spec.pdf` if you ran Step 0).
3. Save the resulting JSON block in your local repository as `my_spec.json`.
4. (Optional) Review the JSON file. If the AI misunderstood any columns or custom types, edit them directly in the JSON file.

### Step 2: Compile the JSON to ETL-SQL Boilerplate

Run the CLI command `gen-script` to compile the JSON schema definition:

```powershell
etl-sql gen-script --schema ./my_spec.json --output ./Scripts/load_feed.etlsql
```

Before writing any `.etlsql` file, `gen-script` validates the JSON against the specification contract. The machine-readable contract is checked in at [spec_pipeline.schema.json](spec_pipeline.schema.json). If the AI output is missing required fields, mixes root-level `schema` with `datasets`, has unsupported enum values, duplicate column names, invalid numeric bounds, or a malformed `validation_regex`, the command prints the validation errors and stops.

The command generates a pre-formatted ETL-SQL script containing:
*   Header blocks with metadata descriptions, owner information, and security classifications.
*   AI extraction review notes for confidence scores and source evidence when the JSON includes them.
*   Source layout notes for file format, headers, skipped rows, null tokens, keys, duplicate policy, fixed-width positions, date formats, and allowed values when the vendor spec provides them.
*   Outbound connection declarations (e.g. `CREATE CONNECTION ... AS FLATFILE`) mapped from the spec.
*   Cleansing and casting statements (e.g. `TRY_CAST`, `SUBSTRING`) for every target column.
*   Declarative inline Data Quality rules (`EXPECT <rule> ON FAILURE <action>`) and statement-level `ON FAILURE` routing (e.g. `ON FAILURE QUARANTINE TO #rejected_data;`).
*   Lineage tagging declarations using `TAG` (see [Lineage.md](../reference/statements/session-control/lineage.md)).
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

For a complete worked example, see [Specification-Driven Vendor Feed Build](../cookbooks/etl/spec-driven-vendor-feed.md) and the runnable sample [realworld_12_spec_driven_customer_feed.etlsql](../../samples/07_Real_World/realworld_12_spec_driven_customer_feed.etlsql).

---

## Validation Gates

At runtime, the script executes safety assertions before uploading data:

1.  **Type & Alignment Gate:** The `EXPECT SCHEMA` check ensures the extraction query output `#staging` is structured exactly as the target spec expects, preventing unexpected upstream shifts from crashing database inserts.
2.  **Declarative Data Quality Gate:** Column-level `EXPECT` rules enforce value constraints (e.g. `NOT NULL`, `UNIQUE`, `MATCHES <regex>`, `IN (<allowed>)`, `> 0`), while the clause's `ON FAILURE` names the column action (`THROW`, `WARN`, `QUARANTINE`).
3.  **Statement-Level Reject Routing:** With `source.reject_policy = "quarantine"`, the projection uses `ON FAILURE QUARANTINE TO #rejected_data;` so clean records flow to `#cleaned_data` while non-compliant records route to `#rejected_data`. With `fail_batch`, `ON FAILURE THROW;` halts execution immediately. With `warn`, `ON FAILURE WARN;` records warnings without interrupting the pipeline.
4.  **Governance Tagging:** The derived columns automatically inherit classification properties like `@pii`, `@key`, or `@confidential` and push them downstream to trace data lineage.


