# COALESCE

Returns the first non-NULL value from an ordered list of expressions. Evaluates arguments sequentially and short-circuits upon encountering the first non-NULL value.

---

## Syntax

### 1. Standard Function Form
```sql
COALESCE(value1, value2 [, ... valueN])
```

### 2. Null-Coalescing Operator Shorthand (`??`)
```sql
expression1 ?? expression2 [?? expressionN]
```

---

## Parameters & Return Type

- **`value1, value2, ...`** — Expressions of compatible data types. Supports literals, columns, subqueries, and scalar functions.
- **Return Type**: Inferred from the highest precedence type among the provided arguments (e.g. `VARCHAR` and `INT` yields compatible string conversion; `INT` and `DECIMAL` yields `DECIMAL`).
- **Null Behavior**: Returns `NULL` if and only if **all** evaluated arguments are `NULL`.

---

## Examples

### 1. Multi-Tier Display Name Hierarchy & `??` Shorthand

Cascade through preferred user identity fields down to a static fallback:

```sql
SELECT 
    user_id,
    -- Standard COALESCE
    COALESCE(preferred_name, first_name, username, 'Guest') AS display_name,
    -- Equivalent shorthand syntax
    preferred_name ?? first_name ?? username ?? 'Guest' AS display_name_compact
FROM #users;
```

### 2. Cleaning Whitespace & Empty Strings

Combine with `NULLIF` and `TRIM` to normalize blank string inputs:

```sql
-- Converts whitespace-only or empty strings into clean default labels
SELECT 
    customer_id,
    COALESCE(NULLIF(TRIM(region_code), ''), 'UNKNOWN') AS region
FROM #dirty_imports;
```

### 3. Production ETL: Multi-Source Customer Address Consolidation

Extract customer records from both CRM (Postgres) and Billing (MSSQL), reconciling missing shipping and contact addresses into a unified master staging table:

```sql
CREATE CONNECTION crm_db     AS POSTGRES(HOST='crm.internal', DATABASE='crm');
CREATE CONNECTION billing_db AS MSSQL(SERVER='billing.internal', DATABASE='invoicing');

-- 1. Extract CRM profiles and billing contact information
SELECT id AS crm_id, email, shipping_address, phone INTO #crm_cust FROM crm_db.contacts;
SELECT customer_key, billing_address, phone_backup INTO #billing_cust FROM billing_db.dbo.Accounts;

-- 2. Consolidate contact data with fallback priority: Shipping Address -> Billing Address -> Default
SELECT 
    c.crm_id,
    c.email,
    -- Resolve best available address
    c.shipping_address ?? b.billing_address ?? 'No Address On File' AS master_address,
    -- Resolve contact phone
    c.phone ?? b.phone_backup ?? 'Unlisted' AS contact_phone,
    -- Flag whether records were matched across both systems
    (b.customer_key IS NOT NULL => 'Cross-System Verified' : 'CRM Only') AS match_status
INTO #master_customers
FROM #crm_cust AS c
LEFT JOIN #billing_cust AS b ON c.crm_id = b.customer_key;
```

---

## Remarks & Short-Circuit Optimization

- **Short-Circuiting**: Arguments are evaluated from left to right. Once a non-NULL value is found, remaining expressions are not evaluated.
- **Dialect Shorthand**: The `??` operator is compiled directly to `COALESCE` during AST construction and has identical runtime performance and semantics.

---

## References & Related Recipes

- [Expressions and Operators](../../statements/expressions-and-operators.md)
- [ISNULL](isnull.md)
- [NULLIF](nullif.md)
- [IIF](../conversion/iif.md)
- [CASE Expression](../../statements/query-syntax/case.md)
- [ETL Cookbook: Cross-Platform Reconciliation](../../../cookbooks/etl/cross-platform-reconciliation.md)
- [ETL Cookbook: Staged Ingestion](../../../cookbooks/etl/staged-ingestion.md)
- [Syntax Index](../../../syntax-index.md)
