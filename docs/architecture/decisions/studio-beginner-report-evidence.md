# Studio Beginner Report Journey Evidence

**Tasks:** ST16 and ST17 automated coverage; learner explanation still pending.
**Run:** 2026-10-11 on Windows, .NET 10.0.401, Node 22.23.3 and npm 10 tooling.
**Source:** working tree based on d799b7915bbffa642b56eeb8638871f7521c5d72, with the changes in
[StudioContinuousJourneyTests](../../../tests/ETL-SQL.Portal.BrowserTests/StudioContinuousJourneyTests.cs).
**Result:** two passed, zero failed, zero skipped. This is a focused run, not release certification.

## Verified sequence

Both tests run the same authoring actions against a production host with real analysis, patching and
execution responses. Setup provides an existing sample report and MOCKDB connection; this does not
certify creation from Home or a connection-configuration journey.

1. Select sample data, drag a BAR visual onto the canvas and assign its roles through field controls.
2. Apply a filter, open Split view and check both the visual and code panes are visible.
3. Open the visual helper, inspect its SQL preview and nonempty explanation, then cancel without
   changing the buffer.
4. Use the editor and keyboard input to append a comment and a simple query with a known result.
5. Reopen/cancel the helper and compare the entire buffer with its pre-helper capture.
6. Select the typed query through keyboard controls, run it and assert the single expected column
   and numeric value, 42.
7. Capture text independently before Save. Compare it with host-returned text and the buffer after
   an actual browser reload. Only CRLF/LF normalization is allowed; trailing text is not trimmed.
8. Execute the saved report through the host-specific path below.

| Test | Identity and persistence | Saved-report execution |
| --- | --- | --- |
| Portal_DrivesTheContinuousJourneyFromConnectToRun | A newly created user has exactly the Publisher role and owns the test folder. A separate Admin session provisions the shared MOCKDB connection only. The Publisher creates, edits, saves and reopens the catalog report. | Opens the report viewer, executes/refreshes and verifies a rendered UsersTable row. This proves a rendered report, not every chart's numeric correctness or a separate reader/RLS identity. |
| Desktop_DrivesTheContinuousJourneyFromConnectToRun | Authenticated WorkstationEditor host with its temporary project; no external editor. Reads the saved file, reloads the browser and compares its full editor text. | Runs the complete saved script and asserts the appended query returns the expected column and value. |

The certification helper now receives the independently captured pre-save script and the host-returned
script. Previously these tests supplied the same reloaded string twice, which could not detect changes
during persistence. Portal setup also declares its shared connection in the script so the saved report
can execute outside the author's editing session.

## Validation

Prepared pinned browser dependencies, ran asset preparation, and passed its read-only Check mode.
The focused command was:

~~~powershell
dotnet test tests\ETL-SQL.Portal.BrowserTests\ETL-SQL.Portal.BrowserTests.csproj --filter "FullyQualifiedName~StudioContinuousJourneyTests" --logger "trx;LogFileName=studio-continuous.trx" --results-directory .tmp\studio-journey-results
~~~

The local TRX is in .tmp/studio-journey-results/studio-continuous.trx. Its counters record two executed
and two passed tests. No browser runtime source or permission policy was changed for these tests.

## Remaining acceptance

ST16/ST17 remain open for the learner's own explanation of the generated SQL. Automation checks that
the helper presents an explanation and that the workflow preserves and executes text; it cannot show
that a learner understood it. Record a short observed walkthrough against this sequence: ask the
learner what the visual's source and mappings mean, have them explain the code edit, and note help
needed or confusing steps. Carry the observation into ST03.

Home creation, paginated parameter binding, exact dashboard aggregates, separate reader/RLS behavior,
and the broader report/ETL matrices retain their own acceptance work. See the
[report inventory](studio-report-authoring-inventory.md) and
[script/ETL inventory](studio-script-etl-authoring-inventory.md).
