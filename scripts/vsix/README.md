# Unsigned VSIX toolchain

Use Node.js 22 and npm 10. Restore from this folder with `npm ci`; the lockfile is
required. `node scripts/test-vsix-toolchain.mjs` from the repository root checks
the restored toolchain and creates real fixture archives for all four targets.
The consumer gate runs this check too. Restore this folder before running pre-push.

`@vscode/vsce` is the MIT archive packager. Its proprietary signing dependency is
replaced by the repository-owned `unsigned-signing` module through a direct local
dependency and an npm override. No signing binary is installed. Every signing or
verification call throws; this toolchain creates unsigned archives only.

The loader in `scripts/lib/vsix-toolchain.cjs` verifies the manifest, lock, restored
versions/integrities, dependency licenses, and the signing-module resolution before
loading the packager. It never invokes npm or downloads a fallback tool. Archive
inspection uses the direct, locked MIT dependencies `yauzl` and `xml2js`.

The assembly helper requires a freshly built staging directory with no prepublish
script and a new temporary output path. Existing packages are rejected. The archive
reader checks the actual extension/VSIX identity and target, entry-point presence,
required file hashes, unsafe/duplicate paths, and development output. It streams
binary hashes so a large VSIX does not need to fit in memory.

The PowerShell/Bash build and publish wrappers delegate to `scripts/package-vsix.mjs`.
It restores the four package lockfiles, prepares browser assets, always rebuilds the
React UI and extension in fresh directories, and assembles only runtime payloads.
Build mode publishes the four .NET tools separately. Publish mode reads existing
tools without changing their source directory. The BCL inspector checks actual
versions, native architectures, self-contained bundles, and required SQLite bindings.
Player browser files must match the prepared candidate. An input fingerprint detects
source changes during packaging.

The archive contains an inspection receipt and retains dependency licenses. Executable
ZIP modes are set explicitly for cross-platform packaging. The streamed archive check
verifies file-selection hashes, executable permissions, and identity/target metadata
before an atomic rename replaces the accepted VSIX. Failed stages preserve that file.
Development `bin`, `out`, and `ui/dist` folders remain untouched. A workspace lock
serializes packaging; an abandoned lock requires manual inspection before retrying.

Complete product/webview runtime acceptance and final source-control cutover checks
remain open in `TODO.md` §1.1. Archive metadata alone does not close those checks.
