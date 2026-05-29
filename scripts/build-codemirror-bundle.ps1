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

param(
    [string]$Version = "6.0.1"
)

$ErrorActionPreference = "Stop"
$Root    = Split-Path -Parent $MyInvocation.MyCommand.Definition | Split-Path -Parent
$OutDir  = Join-Path $Root "src\ETL-SQL.ReportRuntime\Resources\Shared\designer\codemirror"
$TmpDir  = Join-Path $env:TEMP "etlsql-cm-build-$(Get-Random)"

Write-Host "Building CodeMirror bundle..." -ForegroundColor Cyan
Write-Host "  Temp dir : $TmpDir"
Write-Host "  Output   : $OutDir"

try {
    New-Item -ItemType Directory -Force -Path $TmpDir | Out-Null
    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

    # Minimal package.json for the temp build workspace
    @"
{
  "name": "cm-build",
  "version": "1.0.0",
  "type": "module",
  "private": true
}
"@ | Set-Content (Join-Path $TmpDir "package.json") -Encoding UTF8

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
"@ | Set-Content (Join-Path $TmpDir "entry.js") -Encoding UTF8

    Write-Host "  Installing CodeMirror packages..." -ForegroundColor Yellow
    Push-Location $TmpDir
    npm install --silent `
        "@codemirror/state@$Version" `
        "@codemirror/view@$Version" `
        "@codemirror/commands@$Version" `
        "@codemirror/language@$Version" `
        "@codemirror/search@$Version" `
        esbuild
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
