### Fixed

- **Four search boxes had no accessible name.** The admin user filter, the docs dictionary search,
  the governance asset filter, and the quarantine queue search all relied on a `placeholder`. A
  placeholder is not an accessible name — most screen readers announce the control as just "search
  box", and the hint disappears the moment the user starts typing, so the one clue about what the
  field searches is gone precisely when it is needed.

- **`studio.html` presented a dialog with no focus management at all.** Opening it left the keyboard
  user behind it, Tab walked straight out into the page the dialog was supposedly blocking, and
  closing it dropped focus back at the top of the document.

- **Three governance dialogs were dialogs only to sighted users** — no `role="dialog"`, no
  `aria-modal`, no accessible name, no focus trap. An overlay marked up as a plain `div` is announced
  as ordinary page content: the user is never told a dialog opened, and the content behind it stays
  reachable, so the "modal" blocks a mouse user and nobody else.

### Added

- `js/dialog-a11y.js` — shared dialog behaviour: focus moves in on open, Tab stays inside, focus
  returns to the opener on close, and Escape dismisses. It watches for the `style`/`class` changes
  the Portal uses to show a dialog, so a new dialog gets the behaviour without its author needing to
  know the module exists. This existed as three near-identical copies inside `index.html`,
  `admin.html`, and `orchestrator.html`, and not at all in `studio.html` — three copies is not
  redundancy, it is three chances to fix a bug once and still ship it twice.

- `PortalDialogAccessibilityTests` — a source-level sweep over every page and JS module asserting
  every modal overlay is a semantic, named, modal dialog, that no page presents a dialog without
  focus management, and that closed dialogs are hidden by `display`/`visibility` rather than by
  opacity alone. It covers the dialogs no browser test happens to open, which is where this
  regresses. The detector matches overlay classes by *pattern* rather than by a list of known names:
  its first version passed with 31 green assertions while three unmarked dialogs sat behind a
  prefixed class the list did not contain.

- `PortalAccessibilityTests` — a browser lane running every page at both 1440px and 390px, asserting
  what only a browser can compute: the accessible name of every visible interactive control (derived
  the way the accessibility tree derives it), no horizontal page overflow at phone width or at 200%
  text, closed dialogs not tab-reachable, both colour schemes free of text that blends into its
  background, `prefers-reduced-motion` honoured, forced-colours substitution not opted out of, and no
  status chip whose meaning is carried only by colour. Every failure names the offending elements,
  because "3 controls have no accessible name" is a finding nobody can act on.

- `BrowserSession` now records `console.error` output alongside thrown exceptions. The two catch
  different failures: an exception stops a code path, a console error usually does not — which is
  exactly why it survives review.

### Changed

- The browser lane shares one Portal host and one Chromium across all its test classes
  (`ICollectionFixture`) instead of building them per class, and `PortalBrowserFactory` now stops the
  Kestrel host and waits before disposing it — `IHost.Dispose()` only signals shutdown, so teardown
  had been racing the deletion of the temp directory it was still using. Both are real fixes; neither
  resolved the lane's intermittent startup failure, which survives across separate processes and is
  recorded with the current diagnosis in `docs/architecture/decisions/v0.18.0-flaky-tests.md`.
