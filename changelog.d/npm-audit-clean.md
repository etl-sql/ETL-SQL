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

  Fixed in the `overrides` blocks, not just the lockfiles. **The stale overrides were the actual
  defect**: their floors were set to whatever was current when they were written, and that version
  is the one that later became vulnerable — `undici: ">=7.28.0"` admits exactly 7.28.0, and
  `brace-expansion: ">=5.0.6"` admits 5.0.6 through 5.0.8. A lockfile-only fix would have re-broken
  on the next resolve. Floors are now `>=8.10.0` and `>=5.0.9`.

  No direct dependency was added or changed — only override floors and the resulting lockfile
  entries — so the third-party inventory is unaffected.

  The UI package's two advisories had never been reported by CI, because the extension audit runs
  first and failed the job before that step was reached.

  Verified rather than assumed after the bumps — `undici` moved a major version under `jsdom`:
  extension compile, lint and its 6 integration tests; UI lint, build and its 13 unit tests.
