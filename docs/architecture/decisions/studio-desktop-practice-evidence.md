# Studio Desktop Practice Evidence

**Task:** ST22, bounded desktop failure/repair lifecycle.
**Run:** 2026-10-11, Windows, production WorkstationEditor host and Chromium.
**Result:** one passed, zero failed, zero skipped.

[StudioDesktopPracticeTests](../../../tests/ETL-SQL.Portal.BrowserTests/StudioDesktopPracticeTests.cs)
opens a prepared exercise in a disposable local workspace. All subsequent edits and Run/Save actions
use Studio's browser controls. No external service or editor is used.

## Verified behavior

1. MOCKDB users 1 through 3 are staged. A deliberately incorrect transformation filter excludes
   user 3. ASSERT stops execution before load and the browser displays the repair diagnostic.
2. A separate run inspects the engine column catalog and finds none of the exercise's temporary
   tables. Failed-run staging does not carry into the new session.
3. Keyboard editing replaces the script with the repaired filter while retaining the validation.
4. The repaired script checks exactly three loaded rows, with exactly one occurrence of each
   `(UserID, Score)` pair: `(1, 10)`, `(2, 20)`, `(3, 30)`.
5. An assertion first confirms temporary metadata exists. The script drops all three temporary
   tables, then returns a catalog count of zero in that same session. This prevents a vacuous
   cleanup check against a catalog that never exposed temporary tables.
6. Repeating the complete repaired script passes the same exact-value assertions and cleanup check.
   Saving preserves the repaired script on disk, allowing only line-ending normalization.

The initial isolation check assumed querying an absent temporary table would fail. That response was
successful, so the final test checks absence directly through the catalog instead of depending on an
unrelated missing-table error contract.

## Validation and limits

Browser asset preparation and Check mode passed, as did focused C# whitespace formatting and
`git diff --check`. The focused test command was:

```powershell
dotnet test tests\ETL-SQL.Portal.BrowserTests\ETL-SQL.Portal.BrowserTests.csproj --filter FullyQualifiedName~StudioDesktopPracticeTests --logger "trx;LogFileName=studio-practice.trx" --results-directory .tmp\studio-journey-results
```

The local result is `.tmp/studio-journey-results/studio-practice.trx`.
This completes ST22's failure/repair, rows and cleanup sequence under the
[practice contract](studio-practice-execution.md). It does not certify cancellation/timeout cleanup,
hostile-code isolation, persistent destination writes, or the Portal sandbox journey. Those require
separate evidence. Learner understanding remains part of ST03's observed walkthrough; automation
does not establish it. No runtime implementation or permission policy changed.
