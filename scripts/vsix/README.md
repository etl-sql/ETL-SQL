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

The public build/publish wrappers still need integration with this toolchain and
verification of the actual bundled .NET versions, architectures, and runtime closure.
Those delivery tasks remain open in `TODO.md` §1.1.
