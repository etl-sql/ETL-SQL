# Unsigned VSIX toolchain

Use Node.js 22 and npm 10. Restore from this folder with `npm ci`; the lockfile is
required. `node scripts/checks/test-vsix-toolchain.mjs` from the repository root checks
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

The coordinator never removes a lock it did not create: it deletes `artifacts/vsix-package.lock`
only when the file still holds its own token, and a second run fails with "already locked" and
leaves the file as it found it. So a lock left by a killed run stays until someone clears it:

1. Read `artifacts/vsix-package.lock`. It holds `<process id>:<random id>`.
2. Confirm that process is not running. If it is, a packaging run is active; wait for it.
3. Delete the lock. Because the lock is exclusive, no run was active while it existed, so any
   `.vsix-stage-*` folder beside the output VSIX is left over from the killed run and can be
   deleted too.
4. Run packaging again. The previously accepted VSIX was never touched.

Ctrl+C and SIGTERM cancel the active command and its descendants, wait for shutdown,
then remove the owned staging directory and lock. Archive hashing and inspection also
observe cancellation before promotion. The coordinator exits with 130 for Ctrl+C and
143 for SIGTERM. Commands use separate process groups/consoles, and .NET publishes
disable shared build servers so shutdown targets this invocation's processes.

`node scripts/checks/test-owned-command.mjs` checks descendant shutdown and an unrelated peer.
`node scripts/checks/test-vsix-coordinator.mjs` checks cancellation at workflow boundaries.
`node scripts/checks/test-vsix-cli-cancellation.mjs` checks real Unix signal delivery through
an npm lifecycle. On Windows, run that last check with `--console` in a terminal and
press Ctrl+C when the fixture reports ready.

Complete product/webview runtime acceptance and final source-control cutover checks
remain open in `TODO.md` §1.1. Archive metadata alone does not close those checks.
