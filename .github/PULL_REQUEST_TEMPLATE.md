## Description

Provide a brief summary of the changes introduced by this pull request, including their motivation and design rationale.

Fixes # (issue number)

## Type of Change

- [ ] Bug fix (non-breaking change which fixes an issue)
- [ ] New feature (non-breaking change which adds functionality)
- [ ] Breaking change (fix or feature that would cause existing functionality to not work as expected)
- [ ] Documentation update

## Verification Checklist

Before submitting this PR, please check that you have completed the following verification steps:

- [ ] **Compilation**: Project builds cleanly without compile warnings or errors (`dotnet build`).
- [ ] **Tests**: All tests pass (`dotnet test`).
- [ ] **Asset Sync**: If modifying report runtime assets under `src/ETL-SQL.ReportRuntime/Resources/Shared/`, did you run the sync script? (`node .\scripts\sync-assets.js` and `node .\scripts\sync-assets.js -Check`).
- [ ] **Syntax Index**: If modifying C# syntax tokens in `LanguageMetadata.cs`, did you run the index generator? (`node .\scripts\generate-syntax-index.js`).
- [ ] **Doc Sanity**: Documentation tests and link checking pass successfully.

## Security & Guardrail Check

- [ ] I have verified that no plaintext secrets, connection strings, passwords, or `ENC:...` strings are committed in any scripts, help files, or source files.
- [ ] No changes violate the Zero-Trust security boundaries (such as raw drive root actions or raw script file edits).

## Contribution Certification

- [ ] Every commit includes a valid Developer Certificate of Origin `Signed-off-by` line (`git commit -s`), using my real name and an email address I control.
