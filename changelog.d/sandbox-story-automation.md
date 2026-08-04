### Added

- `SandboxStoryTests` — drives every UI-sandbox story and fixture through a real browser, asserting
  each mounts without throwing, logs nothing to the console, and renders something into the stage.

  The sandbox already imports the **canonical** component sources, so mounting a story exercises the
  same file the Portal ships without needing a Portal, a database, or a login. It had only ever been
  run by a person clicking through it, which meant a broken fixture stayed broken until someone
  happened to open that one — and the fixtures people open least are the failure states, exactly
  where a rendering bug is least likely to be noticed and most likely to matter.

### Fixed

- **The VS Code designer sandbox fixture had never worked.** Its webview imported `renderDesigner`
  from the designer module, which exports `createDesigner`. The import threw and the fixture rendered
  nothing. Found by the new automation on its first run.
