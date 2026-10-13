# Studio Keyboard and Assistive Technology Evidence

**Tasks:** ST24, ST25, ST26, ST27, and the groundwork for ST28 and ST29.
**Run:** 2026-10-12, Windows, Chromium, the Portal and the self-installed desktop host.
**Result:** ST24–ST27 pass. ST28 and ST29 are **not run**: no screen reader has spoken these journeys yet.

Tests: [StudioKeyboardJourneyTests](../../../tests/ETL-SQL.Portal.BrowserTests/StudioKeyboardJourneyTests.cs)
(with its `.Zoom.cs` and `.Assistive.cs` parts) and
`Studio_Canvas_MovesAndResizesAVisualFromTheKeyboardAndWritesTheLayout` in
[SandboxStoryTests](../../../tests/ETL-SQL.Portal.BrowserTests/SandboxStoryTests.cs).

## ST24: desktop keyboard editing

`Desktop_KeyboardEditingJourney_CreatesEditsUndoesAndSavesDocument` creates a document from Home,
moves between tabs with the arrow keys, edits, undoes, and saves through the Save as dialog. The
dialog traps Tab, and Escape returns focus to the editor. The saved file is read from disk and
certified.

## ST25: keyboard move and resize

A visual can be placed three ways without a pointer:

| Control | Does |
| :--- | :--- |
| Arrow keys on a focused card | Move one grid cell |
| Shift with an arrow key | Grow or shrink from the right and bottom edges |
| **Col**, **Row**, **W**, **H** in the inspector | Place by typed number |

Proved in the shared sandbox and in the Portal as a Publisher. Both runs Tab to the card, move one
visual four rows down, widen the other to twelve columns, and set a grid column by typing. The
Portal run then saves from the canvas and reopens the report: `STRUCTURE = 'A / B .'` is in the
host's copy and both placements read back equal.

What had to change:

- **Cards could not take focus.** Arrow-key move existed but only fired when focus happened to be
  inside the canvas, which a mouse click never arranged. Cards are now Tab stops, named with their
  placement.
- **There was no keyboard resize.**
- **The inspector's grid fields did not write the script.** They repainted the canvas and stopped,
  so the layout reverted on reload. They now clamp to the grid, write the script, and honor the
  outline's lock.
- **Focus was lost on every step.** The canvas rebuilds its cards on each change. The focused card
  is found again and refocused.
- **Inside Studio, Ctrl+S and Ctrl+Z on the canvas would have fired twice**, once in the canvas and
  once in Studio. Studio now owns them.
- **A refused arrow step used an undo slot.** It no longer does.
- **The sandbox switched stories on Up and Down** even when a story had used the key.

Limit: `STRUCTURE` states proportions, not cells. A layout that does not divide the row evenly
comes back at the nearest proportion after a reload. That is the existing layout model and was
not changed.

## ST26 and ST27: zoom and focus

**Declared zoom settings:** 100%, 150% and 200% of a 1440 × 900 window. Higher zoom is not claimed.

Zoom is reproduced as the layout it causes: 200% is a 720 × 450 CSS-pixel viewport at a device
scale factor of 2. Six cases pass, three per host, ten runs each without a failure.

| Check | Portal | Desktop |
| :--- | :--- | :--- |
| Dialog | Create catalog report | Save as |
| Every Tab stop in the dialog is inside the dialog and inside the window | Pass | Pass |
| Escape returns focus to the control that opened it | Pass | Pass |
| Canvas, Split and Code toggles reachable; switching never drops focus to the page | Pass | Pass |
| Splitter answers Up and Down and keeps focus | Pass | Pass |
| Both split panes at least 80 CSS pixels tall | Pass | Pass |

Found on the way: creating a document left focus on the hidden Home card, so the next Tab started
from the top of the page. Focus now lands on the new document's tab.

## ST28 and ST29: screen-reader groundwork

Not a screen-reader pass. These transcripts record what Chromium gives assistive technology at
each keyboard step: role, name, description, state, and the text that arrived in a live region.
They show the words exist. They do not show how a screen reader says them.

Fixed before recording:

- The script editor was an unnamed text box. It is "Script editor".
- The Canvas, Split, Code and Model toggles did not say which was on. They carry `aria-pressed`.
- **Create** with an empty name did nothing and said nothing. It now says why, in an alert tied to
  the field.
- The Save as dialog announced its error but did not attach it to the field. It does now.

### Portal document journey

| # | Step | Focus lands on | State | Announced |
| ---: | :--- | :--- | :--- | :--- |
| 1 | Focus the new dashboard card on Home | button "New Dashboard Responsive visual board for charts, KPI cards, tables, slicers, cross-filters, and freeform layout." |  |  |
| 2 | Enter: the create dialog opens | textbox "Report name" in dialog "Create catalog report" |  |  |
| 3 | Clear the name, Tab to Create | button "Create" in dialog "Create catalog report" |  |  |
| 4 | Enter: Create is refused | textbox "Report name" in dialog "Create catalog report" | described: Enter a name for the report.; invalid | Enter a name for the report. |
| 5 | Type a name, Tab to the folder | combobox "Folder" in dialog "Create catalog report" | value: /KeyboardFolder-054ee2c7 |  |
| 6 | Tab to Cancel | button "Cancel" in dialog "Create catalog report" |  |  |
| 7 | Tab to Create | button "Create" in dialog "Create catalog report" |  |  |
| 8 | Enter: the document opens | tab "Heard_Report_4894c186c.rptsql" | selected |  |
| 9 | Left arrow: the Home tab | tab "Home" | selected |  |
| 10 | Right arrow: back to the document tab | tab "Heard_Report_4894c186c.rptsql" | selected |  |
| 11 | Tab to the Code view toggle | button "Code" | described: Code View (CodeMirror 6) |  |
| 12 | Enter: Code view, focus in the editor | textbox "Script editor" |  |  |
| 13 | Edit, undo, edit, Ctrl+S: saved | textbox "Script editor" |  | File Saved. Saved /KeyboardFolder-054ee2c7/Heard_Report_4894c186c.rptsql |

### Desktop document journey

| # | Step | Focus lands on | State | Announced |
| ---: | :--- | :--- | :--- | :--- |
| 1 | Focus the new dashboard card on Home | button "New Dashboard Responsive visual board for charts, KPI cards, tables, slicers, cross-filters, and freeform layout." |  |  |
| 2 | Enter: an untitled document opens | tab "untitled_dashboard_1.rptsql (unsaved changes)" | selected |  |
| 3 | Left arrow: the Home tab | tab "Home" | selected |  |
| 4 | Right arrow: back to the document tab | tab "untitled_dashboard_1.rptsql (unsaved changes)" | selected |  |
| 5 | Tab to the Code view toggle | button "Code" | described: Code View (CodeMirror 6) |  |
| 6 | Enter: Code view, focus in the editor | textbox "Script editor" |  |  |
| 7 | Ctrl+S: the Save as dialog opens | textbox "Filename" in dialog "Save as" |  |  |
| 8 | Clear the filename, Tab to Cancel | button "Cancel" in dialog "Save as" |  |  |
| 9 | Tab to Save | button "Save" in dialog "Save as" |  |  |
| 10 | Enter: Save is refused | textbox "Filename" in dialog "Save as" | described: This field is required.; invalid | This field is required. |
| 11 | Type a filename, Save: saved, focus back in the editor | textbox "Script editor" |  | File Saved. Saved heard-report.rptsql |

### Portal canvas layout journey

| # | Step | Focus lands on | State | Announced |
| ---: | :--- | :--- | :--- | :--- |
| 1 | Tab to the table's card | group "UsersTable, TABLE visual, column 7, row 1, 6 columns wide, 4 rows tall" | described: Press Enter to select. Arrow keys move the visual one grid cell. Shift with an arrow key resizes it. |  |
| 2 | Down arrow, four times | group "UsersTable, TABLE visual, column 7, row 5, 6 columns wide, 4 rows tall" | (as above) | UsersTable moved: column 7, row 2, 6 columns wide, 4 rows tall. / … row 3 … / … row 4 … / UsersTable moved: column 7, row 5, 6 columns wide, 4 rows tall. |
| 3 | Shift+Tab to the card visual, Shift+Right arrow six times | group "UserCount, CARD visual, column 1, row 1, 12 columns wide, 4 rows tall" | (as above) | UserCount resized: column 1, row 1, 7 columns wide, 4 rows tall. / … 8 … 11 … / UserCount resized: column 1, row 1, 12 columns wide, 4 rows tall. |
| 4 | Enter selects the table, Shift+Tab to its grid column | spinbutton "Grid column" | value: 7 | UsersTable selected. |
| 5 | Type 1, Tab to the grid row | spinbutton "Grid row" | value: 5 |  |
| 6 | Tab to the table's card, Ctrl+S | group "UsersTable, TABLE visual, column 1, row 5, 6 columns wide, 4 rows tall" | (as above) | File Saved. Saved /KeyboardFolder-10d6f024/Keyboard Layout ….rptsql |

Known rough edges, left for the pass to judge:

- The Home card's name is its title and its whole description in one breath.
- The Code toggle's description says "CodeMirror 6", which means nothing to an author.
- A toast sits inside the notification region and both are live. A screen reader may say it twice.
- Editing a document changes its tab's name to "(unsaved changes)" silently. Nothing announces it.

## Screen-reader pass sheet (ST28, ST29)

**Readiness checked 2026-10-13:** Windows Narrator is installed at
`C:\Windows\System32\Narrator.exe` on the current test machine. No spoken journey has been
observed or recorded. Installation is no longer a prerequisite; the observed passes below remain open.

Closing ST28 and ST29 needs one named screen reader and browser to speak the document journey in
each host, with what it said kept here.

- **Combination:** record the screen reader, its version, the browser and its version. NVDA with
  Chrome on Windows is the suggested one.
- **Journey:** the numbered steps of the Portal and desktop document tables above, in order, with
  the keyboard only.
- **Record:** for each step, what the screen reader said, word for word. NVDA's speech viewer or
  its log at input/output level gives this without retyping.
- **Pass:** each step says a name and a role that match the table, step 4 (Portal) and step 10
  (desktop) speak the error without the author looking for it, and the save is spoken.
- **Findings:** anything said twice, not said, or said in a misleading order. A finding is a result,
  not a failed pass.

| Host | Screen reader and browser | Date | Result | Transcript |
| :--- | :--- | :--- | :--- | :--- |
| Portal (ST28) | not run | | | |
| Desktop (ST29) | not run | | | |

## Validation

```powershell
node scripts\sync-assets.js
node scripts\sync-assets.js -Check
node scripts\typecheck-browser.mjs
node scripts\lint-browser.mjs
dotnet test tests\ETL-SQL.Portal.BrowserTests --filter "FullyQualifiedName~StudioKeyboardJourneyTests|FullyQualifiedName~Studio_Canvas_MovesAndResizes"
```

Set `ETLSQL_ASSISTIVE_TRANSCRIPT_DIR` to a folder to have the three transcripts written there.
