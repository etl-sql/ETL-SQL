# Specification-Driven Pipeline Development

This guide describes how to use specification files (PDFs, Excel workbooks, Word documents, or CSVs) to generate structured, validated ETL-SQL pipelines. 

By leveraging an external AI to parse unstructured documents into a standard JSON schema model, and compiling that model with the `etl-sql gen-script` command, you can dramatically accelerate pipeline creation while enforcing data governance.

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

The C# extraction engine uses heuristic analysis (scanning for database type keywords and column header terms while penalizing API/connectivity jargon) to automatically slice out only the pages containing schemas and output them to the trimmed PDF.

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

The command generates a pre-formatted ETL-SQL script containing:
*   Header blocks with metadata descriptions, owner information, and security classifications.
*   Outbound connection declarations (e.g. `CREATE CONNECTION ... AS FLATFILE`) mapped from the spec.
*   Cleansing and casting statements (e.g. `TRY_CAST`, `SUBSTRING`) for every target column.
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

---

## Validation Gates

At runtime, the script executes safety assertions before uploading data:

1.  **Type & Alignment Gate:** The `EXPECT SCHEMA` check ensures the extraction query output `#staging` is structured exactly as the target spec expects, preventing unexpected upstream shifts from crashing database inserts.
2.  **Length & Format Check:** The cleansing query automatically truncates long strings using `SUBSTRING` and filters malformed values with `TRY_CAST` or regex mappings.
3.  **Governance Tagging:** The derived columns automatically inherit classification properties like `@pii` or `@confidential` and push them downstream to trace data lineage.
