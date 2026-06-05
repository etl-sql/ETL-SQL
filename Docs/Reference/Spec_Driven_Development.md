# Specification-Driven Pipeline Development

This guide describes how to use specification files (PDFs, Excel workbooks, Word documents, or CSVs) to generate structured ETL-SQL starter scripts.

By leveraging an external AI to parse unstructured documents into a standard JSON schema model, and compiling that model with the `etl-sql gen-script` command, you can save the transcription and boilerplate time in pipeline creation while keeping schema validation, governance tagging, validation summaries, and quarantine scaffolding in the generated output. This workflow still expects a developer to review the JSON, complete the source extraction query, and test with real vendor data.

---

## The Workflow

```
┌─────────────────────────┐
│ 1. Data Spec File       │ (Unstructured: PDF/Excel/CSV)
└────────────┬────────────┘
             │
             │ A. Paste data_spec_parser_instructions.md + Spec File into LLM
             ▼
┌─────────────────────────┐
│ 2. Spec JSON Contract   │ (Standardized metadata & columns)
└────────────┬────────────┘
             │
             │ B. Run: etl-sql gen-script -s spec.json -o script.etlsql
             ▼
┌─────────────────────────┐
│ 3. ETL-SQL Template     │ (Includes casting, validation, and tags)
└────────────┬────────────┘
             │
             │ C. Developer writes extraction query into #staging
             ▼
┌─────────────────────────┐
│ 4. Executable Pipeline  │ (Deterministic, schema-safe execution)
└─────────────────────────┘
```

---

## Detailed Step-by-Step

### Step 0: Extract Schema from Large PDFs (Optional)

If you are dealing with a large vendor PDF specification (e.g., 50+ pages containing API credentials, security whitelists, and introduction fluff), you should trim the PDF first. This ensures the LLM's context window focuses exclusively on the data dictionary tables, preventing token limits and extraction drift.

Run the `extract-spec` command:

```powershell
etl-sql extract-spec --input ./Specs/large_vendor_spec.pdf --output ./Specs/trimmed_spec.pdf
```

The C# extraction engine uses heuristic analysis (scanning for database type keywords and column header terms while penalizing API/connectivity jargon) to isolate likely schema pages and output them to the trimmed PDF.

### Step 1: Generate the Intermediate JSON

1. Copy the contents of the prompt instructions in [data_spec_parser_instructions.md](file:///C:/Users/chuck/scratch/ETL-SQL/Docs/data_spec_parser_instructions.md).
2. Paste the prompt text into your AI assistant (e.g. Gemini, ChatGPT, or Claude) and upload the specification document (use the `trimmed_spec.pdf` if you ran Step 0).
3. Save the resulting JSON block in your local repository as `my_spec.json`.
4. (Optional) Review the JSON file. If the AI misunderstood any columns or custom types, edit them directly in the JSON file.

### Step 2: Compile the JSON to ETL-SQL Boilerplate

Run the CLI command `gen-script` to compile the JSON schema definition:

```powershell
etl-sql gen-script --schema ./my_spec.json --output ./Scripts/load_feed.etlsql
```

Before writing any `.etlsql` file, `gen-script` validates the JSON against the specification contract. The machine-readable contract is checked in at [spec_pipeline.schema.json](file:///C:/Users/chuck/scratch/ETL-SQL/Docs/Reference/spec_pipeline.schema.json). If the AI output is missing required fields, mixes root-level `schema` with `datasets`, has unsupported enum values, duplicate column names, invalid numeric bounds, or a malformed `validation_regex`, the command prints the validation errors and stops.

The command generates a pre-formatted ETL-SQL script containing:
*   Header blocks with metadata descriptions, owner information, and security classifications.
*   AI extraction review notes for confidence scores and source evidence when the JSON includes them.
*   Source layout notes for file format, headers, skipped rows, null tokens, keys, duplicate policy, fixed-width positions, date formats, and allowed values when the vendor spec provides them.
*   Outbound connection declarations (e.g. `CREATE CONNECTION ... AS FLATFILE`) mapped from the spec.
*   Cleansing and casting statements (e.g. `TRY_CAST`, `SUBSTRING`) for every target column.
*   Validation review tables for regex and allowed-value checks, with optional quarantine behavior when `source.reject_policy` is `quarantine`.
*   Lineage tagging declarations using `TAG` (see [Lineage.md](file:///C:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Lineage.md)).
*   An `EXPECT SCHEMA` constraint validator (see [Grammar.md](file:///C:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md#L994-L1016)).

### Step 3: Complete the Extraction Query

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

For a complete worked example, see [Cookbook recipe 25](file:///C:/Users/chuck/scratch/ETL-SQL/Docs/Cookbook.md#25-specification-driven-vendor-feed-build) and the runnable sample [realworld_12_spec_driven_customer_feed.etlsql](file:///C:/Users/chuck/scratch/ETL-SQL/samples/07_Real_World/realworld_12_spec_driven_customer_feed.etlsql).

---

## Validation Gates

At runtime, the script executes safety assertions before uploading data:

1.  **Type & Alignment Gate:** The `EXPECT SCHEMA` check ensures the extraction query output `#staging` is structured exactly as the target spec expects, preventing unexpected upstream shifts from crashing database inserts.
2.  **Length & Format Check:** The cleansing query automatically truncates long strings using `SUBSTRING` and records regex or allowed-value failures in `#spec_validation_issues`.
3.  **Reject Handling:** With `source.reject_policy = "quarantine"`, invalid rows are written to `#rejected_data` and only `#valid_data` is uploaded. With `fail_batch`, the script throws after writing validation counts. With `warn`, it prints a warning and continues.
4.  **Governance Tagging:** The derived columns automatically inherit classification properties like `@pii` or `@confidential` and push them downstream to trace data lineage.
