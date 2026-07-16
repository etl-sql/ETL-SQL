# Build the CodeMirror 6 vendor bundle for the ETL-SQL designer.
#
# Produces:
#   src/ETL-SQL.ReportRuntime/Resources/Shared/designer/codemirror/codemirror-bundle.min.js
#
# Run once when upgrading CodeMirror or when the file is missing:
#   .\scripts\build-codemirror-bundle.ps1
#
# After building, run sync-assets to push to portal and VS Code:
#   .\scripts\sync-assets.ps1

param()

$ErrorActionPreference = "Stop"
$Root    = Split-Path -Parent $MyInvocation.MyCommand.Definition | Split-Path -Parent
$OutDir  = Join-Path $Root "src\ETL-SQL.ReportRuntime\Resources\Shared\designer\codemirror"
$ManifestDir = Join-Path $Root "scripts\codemirror"
$TmpDir  = Join-Path $env:TEMP "etlsql-cm-build-$(Get-Random)"

Write-Host "Building CodeMirror bundle..." -ForegroundColor Cyan
Write-Host "  Temp dir : $TmpDir"
Write-Host "  Output   : $OutDir"

try {
    New-Item -ItemType Directory -Force -Path $TmpDir | Out-Null
    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

    Copy-Item -LiteralPath (Join-Path $ManifestDir "package.json") -Destination (Join-Path $TmpDir "package.json")
    Copy-Item -LiteralPath (Join-Path $ManifestDir "package-lock.json") -Destination (Join-Path $TmpDir "package-lock.json")

    # Entry file — re-exports everything the designer needs
    @"
export {
    EditorState,
    Compartment,
} from '@codemirror/state';
export {
    EditorView,
    keymap,
    lineNumbers,
    highlightActiveLine,
    highlightActiveLineGutter,
    drawSelection,
} from '@codemirror/view';
export {
    defaultKeymap,
    history,
    historyKeymap,
    indentWithTab,
} from '@codemirror/commands';
export {
    syntaxHighlighting,
    defaultHighlightStyle,
    StreamLanguage,
    HighlightStyle,
    bracketMatching,
} from '@codemirror/language';
export {
    searchKeymap,
    highlightSelectionMatches,
} from '@codemirror/search';
export {
    autocompletion,
    completionKeymap,
    closeBrackets,
    closeBracketsKeymap,
} from '@codemirror/autocomplete';
export {
    linter,
    lintGutter,
} from '@codemirror/lint';
export { tags } from '@lezer/highlight';
"@ | Set-Content (Join-Path $TmpDir "entry.js") -Encoding UTF8

    Write-Host "  Installing pinned CodeMirror packages..." -ForegroundColor Yellow
    Push-Location $TmpDir
    $npmOut = npm ci 2>&1
    if ($LASTEXITCODE -ne 0) { throw "npm ci failed: $npmOut" }
    Pop-Location

    $EsBuild  = Join-Path $TmpDir "node_modules\.bin\esbuild.cmd"
    $EntryJs  = Join-Path $TmpDir "entry.js"
    $OutFile  = Join-Path $OutDir "codemirror-bundle.min.js"

    Write-Host "  Bundling with esbuild..." -ForegroundColor Yellow
    & $EsBuild $EntryJs `
        --bundle `
        --minify `
        --format=esm `
        "--outfile=$OutFile"

    $sizeKb = [math]::Round((Get-Item $OutFile).Length / 1KB, 1)
    Write-Host "`nDone. Bundle written to:" -ForegroundColor Green
    Write-Host "  $OutFile ($sizeKb KB)" -ForegroundColor Green
    Write-Host "`nNext: run .\scripts\sync-assets.ps1 to push to portal and VS Code." -ForegroundColor Cyan
}
finally {
    if (Test-Path $TmpDir) {
        Remove-Item $TmpDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
