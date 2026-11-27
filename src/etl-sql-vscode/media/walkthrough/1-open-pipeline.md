# A pipeline in one file

The sample pipeline reads orders from the built-in `MOCKDB()` connector, so there is nothing to install or configure:

1. **Extract** with `EXPECT` rules that fail the run on a missing region or a negative total.
2. **Transform** into `#summary`, a temp table that lives in engine memory.
3. **Output** revenue by region.
4. **Lineage**: `eng.lineage` lists the source columns behind each `#summary` column.

Replace `MOCKDB()` with `MSSQL(...)`, `POSTGRES(...)` or `FLATFILE(...)` and the rest of the script stays the same.
