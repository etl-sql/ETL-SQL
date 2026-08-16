# SELECT Modifiers & Ergonomic Conveniences

Modern query ergonomics inspired by DuckDB and Snowflake. Includes inline wildcard projection modifiers (`EXCLUDE`, `REPLACE`, `RENAME`), left-to-right lateral column alias reuse, `ORDER BY ALL`, shorthand `count()`, numeric digit separators, and trailing commas.

---

## Wildcard Star Modifiers

Modify a `*` wildcard projection without manually enumerating every unchanged column. Applied in order: `EXCLUDE`, then `REPLACE`, then `RENAME`.

```sql
-- 1. EXCLUDE: Drops specific sensitive or internal columns
SELECT * EXCLUDE (internal_secret, ssn) FROM users;

-- 2. REPLACE: Preserves all columns while transforming specific fields
SELECT * REPLACE (LOWER(email) AS email, HASHBYTES('SHA256', ssn) AS ssn) FROM users;

-- 3. RENAME: Preserves all columns while assigning new column aliases
SELECT * RENAME (id AS user_id, created_at AS signup_date) FROM users;

-- 4. Combined: Drop, replace, and rename in a single declarative projection
SELECT * 
  EXCLUDE (internal_notes)
  REPLACE (TRIM(full_name) AS full_name)
  RENAME (id AS user_id)
FROM users;
```

---

## Lateral Column Aliases (Left-to-Right Re-use)

Columns declared earlier in a `SELECT` list can be referenced immediately by subsequent expressions in the same list, eliminating repeated formulas and nested subquery views:

```sql
SELECT 
    quantity,
    unit_price,
    quantity * unit_price AS gross_amount,
    gross_amount * 0.15 AS estimated_tax,
    gross_amount + estimated_tax AS total_invoice,
    -- Immediate conditional evaluation using lateral aliases:
    total_invoice > 1000.0 => 'High Value' : 'Standard' AS priority_bucket
FROM #line_items;
```

---

## Ergonomic Dialect Conveniences

### 1. `ORDER BY ALL` / `GROUP BY ALL`
Orders or groups by all projected output columns from left to right:
```sql
SELECT region, product_category, fiscal_year, SUM(revenue) AS total
FROM #sales
GROUP BY ALL
ORDER BY ALL DESC;
```

### 2. Zero-Argument `count()`
Shorthand equivalent to `COUNT(*)`:
```sql
SELECT department, count() AS employee_count
FROM #employees
GROUP BY department;
```

### 3. Numeric Digit Separators (`_`)
Underscores in numeric literals are ignored by the parser to improve readability of large figures:
```sql
DECLARE @max_threshold DECIMAL = 1_500_000.00;
SELECT * FROM #transactions WHERE amount > 50_000;
```

### 4. Permissive Trailing Commas
Trailing commas are permitted at the ends of `SELECT`, `GROUP BY`, `ORDER BY`, and argument lists:
```sql
SELECT 
    customer_id,
    account_status,
    total_spend,
FROM #accounts
GROUP BY 
    customer_id,
    account_status,
    total_spend,;
```

---

## Production ETL Example: PII Masking & Sensitive Wide-Table Export

Extract 50-column user profiles, drop internal debugging flags, mask sensitive identity attributes, and stage for compliance export:

```sql
CREATE CONNECTION prod_db  AS POSTGRES(HOST='pg01.internal', DATABASE='app_db');
CREATE CONNECTION sftp_out AS SFTP(HOST='partner.vendor.com', USER='vendor_etl', KEYFILE='certs/id_rsa');

-- 1. Ingest full record shape into engine memory
SELECT * INTO #raw_profiles FROM prod_db.users;

-- 2. Cleanse and mask using inline star modifiers and lateral aliases
SELECT * 
  EXCLUDE (password_hash, salt, internal_flags, debug_logs)
  REPLACE (
    LOWER(TRIM(email)) AS email,
    LEFT(phone, 3) + '-XXX-' + RIGHT(phone, 4) AS phone,
    HASHBYTES('SHA256', national_id) AS national_id
  )
  RENAME (
    id AS external_user_id,
    created_at AS member_since
  )
INTO #sanitized_profiles
FROM #raw_profiles;

-- 3. Export sanitized dataset directly to encrypted CSV destination
SELECT * INTO #export_feed FROM #sanitized_profiles ORDER BY ALL;
SEND FILE '#export_feed' TO 'outbound/users_sanitized.csv' AT sftp_out;
```

---

## Remarks & Engine Evaluation

- **Engine-Side Resolution**: Star modifiers, lateral column aliases, and `ORDER BY ALL` are evaluated in the ETL-SQL engine context on `#temp` tables and streamed records.
- **Precedence**: Lateral aliases resolve left-to-right. If a source table already contains a column with the same name, the source column takes precedence.

---

## References & Related Recipes

- [Query Syntax Reference](README.md)
- [SELECT Statement](../dml/select.md)
- [GROUP BY ALL](group-by-all.md)
- [CASE Expression](case.md)
- [ETL Cookbook: PII Masking & Hashing](../../../cookbooks/etl/pii-masking-and-hashing.md)
- [ETL Cookbook: Staged Ingestion](../../../cookbooks/etl/staged-ingestion.md)
- [Syntax Index](../../../syntax-index.md)
