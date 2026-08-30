# Specification-Driven Vendor Feed Build
Use this pattern when a vendor gives you a PDF, Excel workbook, or data dictionary and you want a strong ETL-SQL starting point without hand-transcribing every column. The workflow is: extract the vendor schema into JSON, generate the script template, complete the extraction block, then run the pipeline with validation and quarantine review tables.

**Pattern Scenario:** Build a customer feed pipeline from a vendor data specification.

### Step 1: Review the JSON contract

The example contract is checked in at [realworld_12_spec_driven_customer_feed.json](../../../samples/07_Real_World/realworld_12_spec_driven_customer_feed.json). It captures source layout, destination naming, column types, evidence, confidence, PII tags, regex checks, allowed values, and reject policy.

For a real vendor document, copy [data_spec_parser_instructions.md](../../../src/etl-sql-vscode/resources/data_spec_parser_instructions.md) into your AI assistant with the vendor spec. Save the returned JSON and review low-confidence fields before generating a script.

### Step 2: Generate the script template

```powershell
etl-sql gen-script `
  --schema samples/07_Real_World/realworld_12_spec_driven_customer_feed.json `
  --output Scripts/load_vendor_customer_feed.etlsql
```

The generated template includes:
* `EXPECT SCHEMA #staging (...)` from the JSON contract.
* `INSERT TAG FOR TABLE #cleaned_data (...)` lineage metadata.
* Inline `EXPECT <rule> ON FAILURE <action>` data-quality rules, plus `@d: '...'` description tags.
* `ON FAILURE QUARANTINE TO #rejected_data;` when `source.reject_policy` is `quarantine`.

### Step 3: Complete the extraction block

Replace the generated `EXTRACT PHASE` placeholder with the real source query. The runnable sample uses a mock vendor table so it works without external files:

```sql
CREATE TABLE #vendor_file (
    customer_id INT,
    email_address VARCHAR(150),
    customer_status VARCHAR(20),
    signup_date VARCHAR(20)
);

INSERT INTO #vendor_file VALUES (1001, 'alice@example.com', 'ACTIVE', '2026-06-01');
INSERT INTO #vendor_file VALUES (1002, 'bad-email', 'ACTIVE', '2026-06-02');
INSERT INTO #vendor_file VALUES (1003, 'carol@example.com', 'UNKNOWN', '2026-06-03');

SELECT
    customer_id AS CustomerId,
    email_address AS Email,
    customer_status AS Status,
    signup_date AS SignupDate
INTO #staging
FROM #vendor_file;
```

### Step 4: Run the completed script

```powershell
etl-sql run samples/07_Real_World/realworld_12_spec_driven_customer_feed.etlsql
```

Expected result:
* 3 inbound rows are staged and cleaned.
* 2 non-compliant rows (invalid email format and unapproved status) are routed to `#rejected_data`.
* 1 valid row is written to `#vendor_customer_export`.
* `#cleaned_data` and `#rejected_data` are available for downstream load and quarantine review.

Use this pattern as the handoff between AI extraction and production ETL work: the AI accelerates the first draft, while the script still forces schema validation, governance tagging, and explicit review of rejected rows.

