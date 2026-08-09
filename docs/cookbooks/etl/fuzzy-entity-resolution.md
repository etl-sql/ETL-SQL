# Fuzzy Entity Resolution (Customer De-duplication)
Inbound names from third-party feeds rarely match your canonical list exactly. `FUZZY JOIN` matches on a similarity score instead of equality and injects a `__score` column, so you can resolve messy names to canonical ones and route the rest to manual review.

**Pattern Scenario:** Map a noisy vendor feed onto a canonical customer list, then isolate the unmatched rows.

```sql
-- Canonical reference list.
CREATE TABLE #reference (id INT, canonical_name VARCHAR(100));
INSERT INTO #reference VALUES
    (1, 'Acme Corporation'),
    (2, 'Globex Industries'),
    (3, 'Initech LLC');

-- Inbound, messy names from a third-party feed.
CREATE TABLE #dirty (id INT, name VARCHAR(100));
INSERT INTO #dirty VALUES
    (101, 'ACME Corp.'),
    (102, 'Globex Inds'),
    (103, 'Initech'),
    (104, 'Unknown Vendor Co');

-- Best single match per dirty row, scored above 0.70. NORMALIZE('COMPANY')
-- strips the legal/punctuation noise so similarity reflects the core name.
SELECT d.id, d.name AS raw_name, r.canonical_name, __score
INTO   #resolved
FROM   #dirty d
FUZZY JOIN #reference r
    ON SIMILARITY(NORMALIZE(d.name, 'COMPANY'), NORMALIZE(r.canonical_name, 'COMPANY')) > 0.70
    KEEP BEST 1;

-- Rows that matched nothing (LEFT keeps them with NULL canonical_name) are
-- the manual-review queue.
SELECT d.id, d.name AS raw_name, r.canonical_name, __score
FROM   #dirty d
LEFT FUZZY JOIN #reference r
    ON SIMILARITY(d.name, r.canonical_name) > 0.70
    KEEP BEST 1;
```

> Use `KEEP BEST 3` and `ORDER BY __score DESC` instead when you want the top few candidates per row for a human to adjudicate. See [Reference/Grammar.md](../../guides/onboarding/getting-started.md) §5.4 for the full `FUZZY JOIN` options and the `SIMILARITY` / `LEVENSHTEIN` / `NORMALIZE` functions.
