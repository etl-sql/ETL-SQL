# Cookbooks

Cookbooks contain complete, runnable examples. Prefer a cookbook recipe when a reader needs a
production-shaped pattern rather than a small syntax snippet.

## Recipe Collections

- [ETL Recipes](etl/README.md) — extract, stage, validate, transform, merge, cleanup, notify, and govern data movement. 29 recipes.
- [Report Recipes](report/README.md) — report authoring, datasets, visuals, filters, export, and portal publishing patterns. 12 recipes.

Each recipe is its own file, so it can be linked to directly, revised without touching its
neighbours, and found by filename.

## Recipe Standard

Every recipe should include:

- Goal and scenario.
- Required connectors and assumptions.
- Complete script.
- Validation step.
- Cleanup or rollback guidance.
- Operational notes for scheduling, secrets, lineage, and WHAT_IF behavior where relevant.

Use [Cookbook Recipe Template](../templates/cookbook-recipe-template.md) for new recipes. Add the
new file to the collection's `README.md` — that index is the only place a recipe is announced, so a
recipe missing from it is invisible.
