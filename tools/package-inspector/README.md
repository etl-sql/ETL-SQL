# Package binary inspector

This build-time tool reads the actual .NET single-file bundles supplied to VSIX
packaging. It uses the BCL and adds no NuGet dependency. It does not execute foreign
binaries or ship inside the extension.

```powershell
dotnet build tools\package-inspector\ETL-SQL.PackageInspector.csproj -c Release --artifacts-path artifacts\package-inspector
dotnet artifacts\package-inspector\bin\ETL-SQL.PackageInspector\release\ETL-SQL-PackageInspector.dll C:\absolute\ETL-SQL.exe ETL-SQL 0.21.0 win-x64
```

Arguments are the absolute binary path, expected assembly name, release version,
and RID. Supported targets are `win-x64`, `linux-x64`, `osx-x64`, and `osx-arm64`.
Success writes JSON to stdout. Rejection writes an error object to stderr and exits
with status 1.

The reader checks the PE/ELF/Mach-O architecture, bundle manifest, main assembly's
informational version, self-contained runtime configuration, managed core library,
and dependency target. It handles compressed metadata, rejects unsafe/duplicate
entries and oversized metadata, and compares whole-file hashes before and after
inspection. The result includes the actual binary hash and additional native
bindings. CoreCLR is statically linked into the SDK single-file host.

`node scripts/test-package-inspector.mjs` builds real SDK fixtures for the current
host and runs focused rejection checks. The consumer gate discovers this test.
`--all-platforms` publishes fixtures for all four RIDs and checks their metadata;
native execution covers the running host. These checks do not replace the complete
VSIX, product runtime, webview, and delivery-platform acceptance in `TODO.md` §1.1.

Format references:

- [Runtime bundle manifest](https://github.com/dotnet/runtime/blob/main/src/installer/managed/Microsoft.NET.HostModel/Bundle/Manifest.cs)
- [Runtime bundle entries](https://github.com/dotnet/runtime/blob/main/src/installer/managed/Microsoft.NET.HostModel/Bundle/FileEntry.cs)
- [Native bundle marker](https://github.com/dotnet/runtime/blob/main/src/native/corehost/apphost/bundle_marker.c)
- [Static runtime host](https://github.com/dotnet/runtime/blob/main/src/native/corehost/apphost/static/CMakeLists.txt)
