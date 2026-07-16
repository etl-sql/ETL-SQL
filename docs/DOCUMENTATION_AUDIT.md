# Documentation Audit

This audit tracks the remaining work to finish the `docs/` reconfigure from `Docs_Legacy/` into a concise, navigable, modern documentation library.

## Executive Summary

The new `docs/` tree covers most legacy filenames, but it is not yet a finished documentation platform. The biggest gaps are navigation, duplicate pages, stale links and terminology from the old `Docs/` layout, thin reference pages, and missing section-level templates.

The highest-value next move is to create a documentation information architecture: a root `docs/README.md`, section `README.md` files, and a small set of reusable page templates. After that, fill thin reference pages using the templates and retire duplicated aliases.

## Progress

Completed in the first cleanup pass:

- Added `docs/README.md`.
- Added section landing pages for the major guide, reference, cookbook, architecture, and release-support areas.
- Added reusable templates for functions, connectors, statements, visuals, guides, cookbook recipes, architecture docs, and decision records.
- Added snippet documentation entry points: `snippets/README.md` for maintainers and `docs/reference/snippets/README.md` for users.
- Added aggregate/window navigation so aggregate functions used with `OVER (...)` remain discoverable from the window function area without duplicating pages.
- Adopted lowercase documentation filenames for function reference pages while preserving SQL casing in page titles.
- Expanded thin reference pages into full pages: `CAST`, `CONVERT`, `ENV`, `FILE_EXISTS`, `IS_NULL`, `TRY_CONVERT`, `CHAR_LENGTH`, `LENGTH`, `SUBSTR`, `FIRST_VALUE`, `LAST_VALUE`, `POW`, `CEIL`, `RANDOM`, `ERROR_MESSAGE`, `ERROR_NUMBER`, `ERROR_STATE`, `ERROR_LINE`, `ERROR_SEVERITY`, `VAR`, `VARP`, `IFNULL`, `QUOTIENT`, `ATAN2`, `NEWSEQUENTIALID`, `XMLEXISTS`, `XMLQUERY`, `SORT_LIST`, `DIRECTORY`, `LISTAGG`, `ADD_TO_LIST`, `APPEND_TO_LIST`, `BINARY_CHECKSUM`, `STDEVP`, `DIRECTORY_EXISTS`, `IS_NOT_NULL`, `REMOVE_FROM_LIST`, `PERCENTILE_DISC`, `OVERLAY`, `STDDEV`, `EXTRACTVALUE`, `OPENJSON`, `SYSDATE`, `JSON_EXTRACT`, `CURRENT_TIME`, `DATALENGTH`, `DMETAPHONE`, `PI`, `XMLELEMENT`, `JSON_TABLE`, `XMLTABLE`, `JSON_EXISTS`, `JSON_ARRAY`, `CURRENT_DATE`, `XMLATTRIBUTES`, `JSON_OBJECT`, `TO_DATE`, `DATETIMEOFFSETSFROMPARTS`, `REGEXP_MATCHES`, `GET_TAGS`, `LEVENSHTEIN`, `NGRAM_TOKENS`, `REGEXP_SPLIT_TO_TABLE`, `DEGREES`, `RADIANS`, `DMETAPHONE_ALT`, `GET_TAG_VALUE`, `HAS_TAG`, `METAPHONE`, `NGRAMS`, `REGEXP_COUNT`, `RELDATE`, `REMOTE_FILE_EXISTS`, `XMLFOREST`, `CURRENT_TIMESTAMP`, `MINUTE`, `NOW`, `SECOND`, `BIT_COUNT`, `BITAND`, `BITNOT`, `BITOR`, `BITSHIFTLEFT`, `BITSHIFTRIGHT`, `BITXOR`, `FILE_MODIFIED`, `FILE_SIZE`, `PATH_FILENAME`, `RANDOM_DECIMAL`, `RANDOM_INT`, and `HBAR`.
- Removed exact duplicate guide pages and kept one canonical path for each topic.
- Removed the duplicate markdown copy under `docs/assets/`.
- Updated the repository `README.md` documentation map to point into `docs/`.
- Removed active guide/reference/release links to old `Docs/` and `.worktrees/enterprise-policy-hardening/Docs` paths.
- Verified no broken local markdown links were found after the pass.
- Verified no exact duplicate markdown files remained after the pass.
- Verified no mojibake replacement artifacts remained under `docs/` after the latest pass.
- Normalized encoding artifacts in `docs/reference/statements/grammar.md`.

## P0: Navigation And Entry Points

- `docs/README.md` is present.
- Major sections now have local landing pages:
  - `docs/architecture/`
  - `docs/architecture/decisions/`
  - `docs/architecture/standards/`
  - `docs/cookbooks/`
  - `docs/guides/`
  - `docs/reference/`
  - `docs/reference/connectors/`
  - `docs/reference/control-flow/`
  - `docs/reference/file-operations/`
  - `docs/reference/functions/`
  - `docs/reference/orchestrator-jobs/`
  - `docs/reference/portal-admin/`
  - `docs/reference/statements/`
  - `docs/reference/variables-parameters/`
  - `docs/reference/visuals-reporting/`
- `docs/reference/cli/` now has a placeholder README and needs command pages.
- The repository root `README.md` now points its documentation map into `docs/`.

## P0: Stale Old-Tree References

Many new docs still mention old locations such as `Docs/Architecture`, `Docs/Reference`, `Docs/ReleaseNotes`, or `.worktrees/enterprise-policy-hardening/Docs/...`.

Fixed representative examples:

- `docs/Syntax_Index.md` links to `.worktrees/enterprise-policy-hardening/Docs/User_Manual.md`.
- `docs/guides/administration.md` links to `.worktrees/enterprise-policy-hardening/Docs/Orchestrators_Guide.md` and `.worktrees/enterprise-policy-hardening/Docs/User_Manual.md`.
- `docs/guides/getting-started.md` links to old Orchestrator, VS Code architecture, and docs map paths.
- `docs/cookbooks/etl-recipes.md` still references `.worktrees/enterprise-policy-hardening/Docs/User_Manual.md`.
- `docs/reference/dates-times/dates-times.md` links to `.worktrees/enterprise-policy-hardening/Docs/Reference/RelativeDate_Parameters.md`.
- Release files in `docs/releases/` use old "User Manual" paths.

Remaining stale `Docs/` references are concentrated in historical architecture roadmap/decision text and should be classified or rewritten as those files are modernized.

## P0: Thin Reference Pages

Several reference pages exist but are not complete enough to be authoritative. The stated documentation standard requires signatures, return types, and copy-pasteable examples for functions. Many short function pages miss one or more of those sections.

Thin-page batches completed:

- `docs/reference/functions/conversion/convert.md`
- `docs/reference/functions/general/env.md`
- `docs/reference/functions/general/file_exists.md`
- `docs/reference/functions/general/is_null.md`
- `docs/reference/functions/general/try_convert.md`
- `docs/reference/functions/string/char_length.md`
- `docs/reference/functions/string/length.md`
- `docs/reference/functions/string/substr.md`
- `docs/reference/functions/window/first_value.md`
- `docs/reference/functions/window/last_value.md`
- `docs/reference/functions/general/stddev.md`
- `docs/reference/functions/json-xml/extractvalue.md`
- `docs/reference/functions/json-xml/openjson.md`
- `docs/reference/functions/datetime/sysdate.md`
- `docs/reference/functions/json-xml/json_extract.md`
- `docs/reference/functions/datetime/current_time.md`
- `docs/reference/functions/string/datalength.md`
- `docs/reference/functions/general/dmetaphone.md`
- `docs/reference/functions/math/pi.md`
- `docs/reference/functions/json-xml/xmlelement.md`
- `docs/reference/functions/json-xml/json_table.md`
- `docs/reference/functions/json-xml/xmltable.md`
- `docs/reference/functions/json-xml/json_exists.md`
- `docs/reference/functions/json-xml/json_array.md`
- `docs/reference/functions/json-xml/isjson.md`
- `docs/reference/functions/json-xml/json_get.md`
- `docs/reference/functions/json-xml/json_get_text.md`
- `docs/reference/functions/json-xml/json_modify.md`
- `docs/reference/functions/json-xml/json_query.md`
- `docs/reference/functions/json-xml/json_value.md`
- `docs/reference/functions/datetime/current_date.md`
- `docs/reference/functions/json-xml/xmlattributes.md`
- `docs/reference/functions/json-xml/json_object.md`
- `docs/reference/functions/general/to_date.md`
- `docs/reference/functions/general/datetimeoffsetsfromparts.md`
- `docs/reference/functions/general/regexp_matches.md`
- `docs/reference/functions/general/get_tags.md`
- `docs/reference/functions/general/levenshtein.md`
- `docs/reference/functions/general/ngram_tokens.md`
- `docs/reference/functions/general/regexp_split_to_table.md`
- `docs/reference/functions/math/degrees.md`
- `docs/reference/functions/math/radians.md`
- `docs/reference/functions/general/dmetaphone_alt.md`
- `docs/reference/functions/general/get_tag_value.md`
- `docs/reference/functions/general/has_tag.md`
- `docs/reference/functions/general/metaphone.md`
- `docs/reference/functions/general/ngrams.md`
- `docs/reference/functions/general/regexp_count.md`
- `docs/reference/functions/general/reldate.md`
- `docs/reference/functions/general/remote_file_exists.md`
- `docs/reference/functions/json-xml/xmlforest.md`
- `docs/reference/functions/json-xml/xmlvalue.md`
- `docs/reference/functions/datetime/current_timestamp.md`
- `docs/reference/functions/datetime/minute.md`
- `docs/reference/functions/datetime/now.md`
- `docs/reference/functions/datetime/second.md`
- `docs/reference/functions/general/bit_count.md`
- `docs/reference/functions/general/bitand.md`
- `docs/reference/functions/general/bitnot.md`
- `docs/reference/functions/general/bitor.md`
- `docs/reference/functions/general/bitshiftleft.md`
- `docs/reference/functions/general/bitshiftright.md`
- `docs/reference/functions/general/bitxor.md`
- `docs/reference/functions/general/file_modified.md`
- `docs/reference/functions/general/file_size.md`
- `docs/reference/functions/general/path_filename.md`
- `docs/reference/functions/general/random_decimal.md`
- `docs/reference/functions/general/random_int.md`
- `docs/reference/functions/math/abs.md`
- `docs/reference/functions/math/acos.md`
- `docs/reference/functions/math/asin.md`
- `docs/reference/functions/math/atan.md`
- `docs/reference/functions/math/ceiling.md`
- `docs/reference/functions/math/cos.md`
- `docs/reference/functions/math/cot.md`
- `docs/reference/functions/math/exp.md`
- `docs/reference/functions/math/floor.md`
- `docs/reference/functions/math/log.md`
- `docs/reference/functions/math/log10.md`
- `docs/reference/functions/math/power.md`
- `docs/reference/functions/math/rand.md`
- `docs/reference/functions/math/round.md`
- `docs/reference/functions/math/sign.md`
- `docs/reference/functions/math/sin.md`
- `docs/reference/functions/math/sqrt.md`
- `docs/reference/functions/math/tan.md`
- `docs/reference/functions/string/ascii.md`
- `docs/reference/functions/string/char.md`
- `docs/reference/functions/string/charindex.md`
- `docs/reference/functions/string/concat.md`
- `docs/reference/functions/string/concat_ws.md`
- `docs/reference/functions/string/instr.md`
- `docs/reference/functions/string/lpad.md`
- `docs/reference/functions/string/lower.md`
- `docs/reference/functions/string/ltrim.md`
- `docs/reference/functions/string/patindex.md`
- `docs/reference/functions/string/position.md`
- `docs/reference/functions/string/repeat.md`
- `docs/reference/functions/string/replicate.md`
- `docs/reference/functions/string/reverse.md`
- `docs/reference/functions/string/rpad.md`
- `docs/reference/functions/string/rtrim.md`
- `docs/reference/functions/string/space.md`
- `docs/reference/functions/string/split_part.md`
- `docs/reference/functions/string/string_split.md`
- `docs/reference/functions/string/unicode.md`
- `docs/reference/functions/string/upper.md`
- `docs/reference/functions/window/rank.md`
- `docs/reference/functions/window/dense_rank.md`
- `docs/reference/functions/window/lag.md`
- `docs/reference/functions/window/lead.md`
- `docs/reference/functions/window/ntile.md`
- `docs/reference/functions/window/row_number.md`
- `docs/reference/functions/aggregate/avg.md`
- `docs/reference/functions/aggregate/count.md`
- `docs/reference/functions/aggregate/max.md`
- `docs/reference/functions/aggregate/min.md`
- `docs/reference/functions/aggregate/stdev.md`
- `docs/reference/functions/aggregate/string_agg.md`
- `docs/reference/functions/aggregate/sum.md`
- `docs/reference/functions/conversion/coalesce.md`
- `docs/reference/functions/conversion/iif.md`
- `docs/reference/functions/conversion/isnull.md`
- `docs/reference/functions/conversion/nullif.md`
- `docs/reference/functions/conversion/try_cast.md`
- `docs/reference/functions/cryptography/checksum.md`
- `docs/reference/functions/cryptography/hashbytes.md`
- `docs/reference/functions/datetime/dateadd.md`
- `docs/reference/functions/datetime/datediff.md`
- `docs/reference/functions/datetime/datepart.md`
- `docs/reference/functions/datetime/datename.md`
- `docs/reference/functions/datetime/date_trunc.md`
- `docs/reference/functions/datetime/day.md`
- `docs/reference/functions/datetime/extract.md`
- `docs/reference/functions/datetime/getdate.md`
- `docs/reference/functions/datetime/hour.md`
- `docs/reference/functions/datetime/month.md`
- `docs/reference/functions/datetime/year.md`
- `docs/reference/functions/general/date_part.md`
- `docs/reference/functions/general/datetimefromparts.md`
- `docs/reference/functions/general/datetrunc.md`
- `docs/reference/functions/general/eomonth.md`
- `docs/reference/functions/general/isdate.md`
- `docs/reference/functions/general/parse.md`
- `docs/reference/functions/general/timefromparts.md`
- `docs/reference/functions/general/to_timestamp.md`
- `docs/reference/functions/general/trunc.md`

`docs/reference/functions/conversion/cast.md` and `docs/reference/visuals-reporting/visuals/hbar.md` have been expanded and can be used as starter examples for the new style.

No function reference pages remain under 300 bytes, and the original under-700-byte review queue is complete. Some reviewed pages remain under 700 bytes because the topic is small.

Next function-reference cleanup should be driven by old-format markers instead of byte size. A broad scan still finds older function pages with `**Category:**`, `## See Also`, deep guide anchors, and inline output-arrow examples. Continue scanning category-by-category, then do the dedicated taxonomy move pass for files still misplaced under `general/`.

Window aggregate note: aggregate functions such as `SUM`, `AVG`, `COUNT`, `MIN`, `MAX`, and `STDEV` already document their `OVER (...)` forms in their aggregate pages. Keep those pages as the source of truth and link to them from `docs/reference/functions/window/README.md`; do not duplicate full pages in the window folder unless behavior diverges.

## P1: Duplicate Pages

These exact duplicates were consolidated into one canonical page per topic. Non-canonical guide files were deleted rather than kept as compatibility pointers:

- `docs/guides/pipelines-and-dags.md`
- `docs/guides/pipelines-dags.md`
- `docs/guides/report-portal-user-guide.md`
- `docs/guides/report-portal-user.md`
- `docs/guides/testing-guide.md`
- `docs/guides/testing.md`
- `docs/assets/data_spec_parser_instructions.md`
- `docs/guides/data-spec-parser-instructions.md`
- `docs/guides/large-data-certification.md`
- `docs/reference/performance/large-data-certification.md`

Preferred rule: keep short, predictable URLs in `guides/`, keep authoritative reference material in `reference/`, and avoid duplicate full copies.

## P1: Legacy Coverage Still Needing Intentional Placement

Most legacy files have some new equivalent, but these legacy topics need deliberate placement or explicit deprecation:

- `Docs_Legacy/User_Manual.md` should become either `docs/guides/user-manual.md` or be fully absorbed by `docs/guides/getting-started.md`.
- `Docs_Legacy/Orchestrators_Guide.md` appears to map to `docs/guides/job-orchestration.md`, but old links still use the legacy title.
- `Docs_Legacy/Administrators_Guide.md` maps to `docs/guides/administration.md`, but the current guide is still very large and should be split into focused operator pages.
- `Docs_Legacy/Report_SQL_Guide.md` maps to `docs/guides/report-sql.md`, but that file is still over 2,600 lines and should be split into report authoring, visual reference, layout, actions, and publishing.
- `Docs_Legacy/Cookbook.md` maps to `docs/cookbooks/etl-recipes.md`, but the cookbook should have a README and recipe template.
- `Docs_Legacy/Reference/Dates_and_Times.md` and `Docs_Legacy/Reference/RelativeDate_Parameters.md` map to `docs/reference/dates-times/`, but stale links still point to legacy names.

## P1: Snippet Documentation And Runtime Assets

- Built-in autocomplete snippets stay in `/snippets` as runtime/editor assets, not under `/docs`.
- `snippets/README.md` now documents snippet frontmatter, placeholder syntax, authoring rules, and build/runtime flow.
- `docs/reference/snippets/README.md` now provides the user-facing guide for built-in and user-defined snippets.
- Old physical `src/ETL-SQL.Core/Resources/Help` copies should be removed only after the project file embeds replacement docs/snippets from `/docs` and `/snippets`, and build/test coverage confirms runtime help and snippet loading still work.

## P1: Function Folder Taxonomy Cleanup

`docs/reference/functions/general/` is still too broad. It contains several well-defined categories that should move into focused folders after the old-format cleanup batches are complete.

Proposed moves:

- Datetime functions: `date_part.md`, `datetimefromparts.md`, `datetimeoffsetsfromparts.md`, `datetrunc.md`, `eomonth.md`, `isdate.md`, `parse.md`, `reldate.md`, `timefromparts.md`, `to_date.md`, `to_timestamp.md`, `trunc.md`
- File/path functions: `directory.md`, `directory_exists.md`, `file_exists.md`, `file_hash.md`, `file_list.md`, `file_modified.md`, `file_size.md`, `path_combine.md`, `path_directory.md`, `path_extension.md`, `path_filename.md`, `remote_file_exists.md`, `remote_file_list.md`
- Regex functions: `regexp_count.md`, `regexp_instr.md`, `regexp_like.md`, `regexp_matches.md`, `regexp_replace.md`, `regexp_split_to_table.md`, `regexp_substr.md`
- Fuzzy matching functions: `difference.md`, `dmetaphone.md`, `dmetaphone_alt.md`, `levenshtein.md`, `metaphone.md`, `ngram_tokens.md`, `ngrams.md`, `normalize.md`, `similarity.md`, `soundex.md`
- Lineage/tag functions: `get_tag_value.md`, `get_tags.md`, `has_tag.md`
- Job state functions: `get_job_state.md`, `set_job_state.md`
- Random/GUID functions: `newid.md`, `newsequentialid.md`, `random.md`, `random_decimal.md`, `random_int.md`
- Bitwise functions: `bit_count.md`, `bitand.md`, `bitnot.md`, `bitor.md`, `bitshiftleft.md`, `bitshiftright.md`, `bitxor.md`

Do the taxonomy move as a dedicated pass with automated link rewriting and docs validation. Avoid moving these files piecemeal while old pages may still reference their current paths.

## P1: Oversized Guides

The reconfigure goal was smaller and more concise documents. Several new files remain monolithic:

- `docs/guides/report-sql.md` is about 2,600 lines.
- `docs/guides/report-portal-admin.md` is about 1,870 lines.
- `docs/guides/administration.md` is about 1,800 lines.
- `docs/guides/getting-started.md` is about 1,880 lines.
- `docs/guides/job-orchestration.md` is about 1,200 lines.
- `docs/reference/statements/grammar.md` is about 3,400 lines.

These should become overview pages that link to focused reference pages and recipes.

## P1: Template Follow-Up

Templates now exist under `docs/templates/`:

- `function-reference-template.md`
- `connector-reference-template.md`
- `statement-reference-template.md`
- `visual-reference-template.md`
- `guide-template.md`
- `cookbook-recipe-template.md`
- `architecture-template.md`
- `decision-record-template.md`
- Release notes continue to use `docs/releases/TEMPLATE.md`.

Minimum sections to preserve as the templates evolve:

- Functions: syntax, parameters, return type, null behavior, examples, dialect notes, see also.
- Connectors: syntax, required options, auth patterns, mutually exclusive options, security notes, examples, troubleshooting.
- Statements: syntax, semantics, examples, errors, security notes, see also.
- Visuals: syntax, mappings, options, interactions, examples, references.
- Guides: audience, prerequisites, workflow, examples, related reference.
- Recipes: goal, complete script, required connectors, validation, cleanup, operational notes.

## P2: Historical Documents Need Labels

The `architecture/roadmaps/` and `architecture/decisions/` folders contain useful history, but many files are copied from legacy planning documents. They need consistent classification at the top:

- Current reference
- Historical design note
- Active roadmap
- Superseded
- Implementation record

`docs/architecture/roadmaps/README.md` starts this work, but the pattern is not applied consistently across all design and decision files.

## P2: Broken-Link And Coverage Automation

Add a docs validation script that checks:

- No links to `.worktrees/enterprise-policy-hardening/Docs`.
- No links to old `Docs/` paths except in historical quotes.
- Every directory with more than five markdown files has `README.md` or `index.md`.
- Every function page includes `Syntax`, `Parameters`, `Returns`, `Example`, and `References`.
- Every connector page includes auth patterns and mutually exclusive option notes.
- Every visual page includes syntax, mappings/options, example, and references.
- No exact duplicate markdown files exist outside explicitly allowed redirects.

## Suggested Work Order

1. Add `docs/README.md` and section READMEs for the main user paths.
2. Fix old-tree links in `README.md`, `docs/guides/*`, `docs/reference/*`, `docs/cookbooks/*`, and `docs/releases/*`.
3. Add the docs templates.
4. Consolidate duplicate pages.
5. Split the oversized guides into overview pages plus focused reference pages.
6. Expand thin function, statement, visual, connector, and portal-admin reference pages.
7. Add automated docs validation to keep the platform from drifting.
