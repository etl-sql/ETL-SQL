### Security

- **Cleared four npm advisories across the VS Code extension and its UI package**, which had been
  failing the CI audit gate. All were transitive **dev** dependencies — the toolchain, not anything
  the extension ships:

  - `brace-expansion` (high, DoS) — reached through `eslint`→`minimatch` and `mocha`→`minimatch` in
    the extension, and `eslint`→`minimatch` in the UI package.
  - `undici` (high, five advisories including response desynchronisation and cross-user information
    disclosure) — reached through `jsdom`.
  - `postcss` (moderate, arbitrary `.map` read via attacker-controlled `sourceMappingURL`) — reached
    through `vite`.

  Both packages now report zero vulnerabilities. `package.json` is untouched in both: the fix is
  lockfile-only, so no direct dependency was added or changed and the third-party inventory is
  unaffected.

  The UI package's two advisories had never been reported by CI, because the extension audit runs
  first and failed the job before that step was reached.

  Verified rather than assumed after the bumps — `undici` moved a major version under `jsdom`:
  extension compile, lint and its 6 integration tests; UI lint, build and its 13 unit tests.
