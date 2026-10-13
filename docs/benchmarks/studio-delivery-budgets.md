# Studio delivery budgets

## Transfer decision

Measured on Windows 11 x64 with Node 22.23.3 on 2026-10-13. Prepare assets with
`node scripts/sync-assets.js`, then run
`node scripts/measure-studio-transfer.mjs docs/benchmarks/studio-transfer-baseline.json`.
The JSON records every unique response in each production host's import closure.
Static imports, literal dynamic imports, shell scripts/styles, and CSS dependencies are included.
CodeMirror and optional designer modules are included even when opened after startup. The
CodeMirror retry query loads the same bytes and is counted once. HTML, authenticated API responses,
report previews, source maps, and network/protocol overhead are excluded. This is an asset budget,
not total first-navigation traffic. Gzip is measured independently per response at level 9;
it does not assert that either host serves compressed responses.

| Host | Assets | Raw bytes | Gzip bytes | Accepted raw ceiling | Accepted gzip ceiling |
| --- | ---: | ---: | ---: | ---: | ---: |
| Portal | 89 | 2,386,443 | 594,105 | 2,625,088 | 653,516 |
| Desktop | 74 | 2,276,279 | 563,571 | 2,503,907 | 619,929 |

Ceilings allow 10% growth over this measured baseline, rounded up. This is enough for bounded
feature additions while requiring review for larger growth. Changing a ceiling requires a fresh
inventory and an explanation in this document. Existing timing and memory budgets stay in force.
ST42 implementation estimate: M, one Node gate over the inventory, an oversized negative fixture,
and integration into the existing consumer-check lane. No asset optimization is included.

## Large-document boundary investigation

Portal's default `DesignerLimits.MaxScriptCharacters` is 200,000 UTF-16 code units, not bytes.
Designer analysis, catalog creation, private drafts, and recovery drafts enforce that setting.
The application does not currently declare a Studio-specific HTTP byte ceiling. Transport limits
depend on Kestrel and any reverse proxy; they must not be described as a 200,000-byte limit.
The pinned fixture is `StudioDeliveryJourneyTests.LargeDocument`: 5,000 numbered ASCII comment
lines plus `SELECT 1 AS Value;`, 5,001 lines and 190,018 characters/UTF-8 bytes. This measures text
editing and tokenization; it is not a 5,000-statement AST benchmark. On the same Windows environment,
Chromium 149.0.7827.55 measured replace-all at 13.3 ms and real analysis at 26.0 ms in Portal;
desktop measured 10.9 ms and 15.1 ms. These are single local measurements, not throughput claims.
The browser tests retain raw JSON under `artifacts/studio-performance/`.

Accepted contract: automatic analysis, completion, hover and canvas synchronization stop above
the host's character ceiling. Portal advertises its configured limit; desktop uses 200,000.
The buffer remains editable, dirty, and intact. A visible diagnostic explains that editor services
are paused and that reducing the text resumes them. The existing server rejects oversized saves;
no client truncation is permitted. Test exactly at and one character above the default boundary,
including non-ASCII text. ST47 implementation estimate: M; ST48: M, shared policy plus host evidence.
No general editor rewrite or new HTTP byte policy is included.

## Uncached production editor

`StudioDeliveryJourneyTests` uses the real Portal and desktop hosts, with Chromium cache disabled
and cleared before each of three document reloads. The Portal fixture has one report opened by ID;
desktop has a local `.rptsql` file with the identical one-table dashboard script. Authentication
and in-process Kestrel are already running.
The stopwatch includes navigation, asset loading, host/session reads and the mounted usable editor.
It excludes sign-in and process launch. No throttled network or production reverse proxy is claimed.

Matching-fixture Windows samples were Portal 159.9 / 130.4 / 163.1 ms and desktop 62.1 / 61.4 / 80.7 ms.
Accepted regression ceilings are 3,000 ms for Portal and 2,000 ms for desktop. The generous local
headroom accommodates shared CI runners without treating the 20-second harness timeout as a
performance budget. Timing evidence on other operating systems remains a CI responsibility.

## Catalog and recovery verification

The Portal browser fixture seeds 2,500 catalog summaries and verifies bounded pages, forward/back
navigation, server search, and a direct report link beyond the first page. A second externally
created report exercises Refresh, rename/move, deleted selection, and refreshed disappearance.
An unrelated dirty buffer must remain byte-for-byte intact. The separate SQLite policy test compares
batched and single-report authorization: Author, Read, authorship upgrades, revoked/no grant, and
foreign-tenant ownership. Discovery fetches at most 51 candidate rows and resolves ACLs in batches;
there is no full-catalog transfer, total-count scan, or live synchronization for Home.

The document tests verify both hosts pause above 200,000 UTF-16 code units and resume on the pinned
fixture. Portal accepts 200,000 non-ASCII comment characters and rejects 200,001 with HTTP 413;
UTF-8 byte size differs, proving that this is a character contract. An oversized Save is refused
without truncating the buffer or clearing dirty state. One controlled analysis 429 with
`Retry-After: 2` explains stale diagnostics and automatically retries the current text.

Verification commands (from the repository root after asset preparation):

```powershell
node scripts/check-consumer-contracts.mjs --only studio-transfer
dotnet test tests\ETL-SQL.Portal.BrowserTests\ETL-SQL.Portal.BrowserTests.csproj --filter "FullyQualifiedName~StudioDeliveryJourneyTests|FullyQualifiedName~StudioCatalogScaleJourneyTests|FullyQualifiedName~StudioDocumentLimitJourneyTests"
dotnet test tests\ETL-SQL.Portal.Tests\ETL-SQL.Portal.Tests.csproj --filter "FullyQualifiedName~StudioCatalogPagingTests"
```

The UI sandbox includes `catalog-paging` and `large-document` stories with browser coverage.
Final focused results are retained under `artifacts/closed-todo-audit/section9-*.trx` and logs.
