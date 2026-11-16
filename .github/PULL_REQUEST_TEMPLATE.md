## Description

Provide a brief summary of the changes introduced by this pull request, including their motivation and design rationale.

Fixes # (issue number)

## Type of Change

- [ ] Bug fix (non-breaking change which fixes an issue)
- [ ] New feature (non-breaking change which adds functionality)
- [ ] Breaking change (fix or feature that would cause existing functionality to not work as expected)
- [ ] Documentation update

## Verification Checklist

Run targeted checks locally matching your change; GitHub Actions runs the full impacted CI lanes as the merge gate:

- [ ] **Compilation**: Clean build passes after browser asset preparation (`npm ci --prefix scripts/typecheck`, `node scripts/sync-assets.js`, `node scripts/sync-assets.js -Check`, `dotnet build ETL-SQL.slnx`).
- [ ] **Targeted Tests**: Targeted tests for changed projects/filters pass (`dotnet test tests/<Project>/<Project>.csproj --filter "..."`).
- [ ] **Browser Assets & Gates**: If modifying shared assets or browser TypeScript/JavaScript, ran `node scripts/sync-assets.js`, `node scripts/sync-assets.js -Check`, `node scripts/typecheck-browser.mjs`, and `node scripts/lint-browser.mjs`.
- [ ] **Syntax Index**: If modifying C# syntax tokens, ran `node scripts/generate-syntax-index.js`.
- [ ] **Doc Sanity**: Documentation links and doc checks pass for doc updates.

## Security & Guardrail Check

- [ ] I have verified that no plaintext secrets, connection strings, passwords, or `ENC:...` strings are committed in any scripts, help files, or source files.
- [ ] No changes violate the Zero-Trust security boundaries (such as raw drive root actions or raw script file edits).

## Contribution Certification

- [ ] Every commit includes a valid Developer Certificate of Origin `Signed-off-by` line (`git commit -s`), using my real name and an email address I control.
