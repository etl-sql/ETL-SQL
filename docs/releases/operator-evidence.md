# Operator release evidence

Release requires a candidate-bound operator submission in addition to hosted CI, independent
workstation certification and certified packages. It validates retained reports and requires an
explicit operator review for every applicability decision. It does not rerun certification suites.
The required rows and uncovered scope live in
[`operator-release-policy.json`](../../scripts/operator-release-policy.json).

The bundle is an operator submission, not trusted-host execution provenance. Deployment and canary
details are checked against their canonical contracts. Conditional certifications retain the
operator's command, host, candidate, execution window and machine-readable report; explicit failed,
dirty, wrong-candidate or development results cannot support a passed row. Applicability and claim
decisions remain the named reviewer's responsibility. Shared SaaS runtime containment cannot be
claimed through this bundle. Live HA/capacity claims still need their actual operator evidence.

## Prepare the checklist

Finish source and metadata changes, push the release branch, then freeze its SHA. Replace `x.y.z`
and `vLAST` below with the candidate version and previous stable release tag:

```powershell
$candidateSha = (git rev-parse HEAD).Trim()
$version = 'x.y.z'
$previousTag = 'vLAST'
$inputRoot = "release-validation/operator-input-$candidateSha"
New-Item -ItemType Directory -Force release-validation | Out-Null
node scripts/operator-release-evidence.mjs template $candidateSha $version $previousTag $inputRoot
```

This creates `checklist.json` with every review and conditional certification **Pending**. It
records the comparison commit and a SHA-256 digest of Git's NUL-delimited changed-path inventory,
including both sides of renames. No review or certification is marked successful automatically.
`candidateRef` defaults to `refs/heads/release/vx.y.z`; adjust it before sealing if using another
supported release branch or `main`. That branch must still point to the frozen candidate.

Collect evidence under the input's `evidence/` directory:

- Preserve the deployment producer's complete `claims-index.json` and all linked run bundles at
  the deployment report path. Both Profile All and Transition All are required. The validator
  rechecks canonical phase plans, invocations, TRX, lifecycle hashes and provider-neutral faults.
- Copy the complete canary run to its report path. The producer must record Release configuration,
  a clean source fingerprint, a real build and release eligibility. `-NoBuild`, Debug and earlier
  reports missing those fields cannot certify a release. Detailed journeys, provisioning and
  credential lifecycle must still match the canonical plan.
- Retain the recovery validation JSON, transcript and candidate CLI `--version` output at the
  recovery row's paths. The report must pass `admin restore --validate --report`, bind paired backup
  IDs and name the candidate backup version. Validation alone does not measure live recovery time.
- Write each review's supporting note and link the retained evidence in its row. Mark a review
  Passed only after completing it. Record the reviewer and UTC review/generation timestamps after
  all reviewed work finishes.
- For each conditional certification, record Passed with its retained report, command, topology
  and `execution` object, or NotApplicable with the diff-based reason and no execution/evidence.
  The execution object names `sourceSha`, `configuration: "Release"`, `host`, `startedAt`,
  `completedAt` and `exitCode: 0`. HA claims require passed fault/recovery and sustained-capacity
  rows; billion-row claims require that certification. HA fault/recovery also requires its
  machine-readable All-gate validation summary.

All review and execution times must be within seven days and no later than their enclosing review.
Keep the template's canonical `uncovered` text. Do not include backup archives, databases, keys,
runtime settings or diagnostic dumps. This report-only bundle rejects binary inputs, links,
unsafe paths, obvious secret material, files above 32 MiB and total payload above 512 MiB.
Complete the redaction review before sealing; a passing scan cannot establish that arbitrary
prose contains no sensitive values.

## Seal and retain the submission

Use a new submission directory. The sealer validates before creating output, hashes every retained
file and rejects changes, extra files or missing payload during subsequent verification:

```powershell
$submission = "release-validation/operator-submission-$candidateSha-$(Get-Date -Format yyyyMMddHHmmss)"
$payload = Join-Path $submission 'payload'
New-Item -ItemType Directory -Path $payload | Out-Null
node scripts/operator-release-evidence.mjs create $inputRoot "$payload/bundle" $candidateSha $version
node scripts/operator-release-evidence.mjs verify "$payload/bundle" $candidateSha $version
$archive = Join-Path $submission 'operator-release-evidence.zip'
[IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $PWD $payload), (Join-Path $PWD $archive))
```

The ZIP's root contains `bundle/`, not an extra parent directory. Keep source material and the
accepted bundle unchanged. Corrected evidence is a new submission; a source fix is a new candidate.
Do not commit evidence into the candidate after certification.

Upload to a separate unpublished draft, following the independent-workstation transport pattern:

```powershell
$evidenceTag = "operator-check-$candidateSha"
gh release create $evidenceTag --draft --target $candidateSha --title "Operator evidence $candidateSha" --notes "Operator-submitted release checklist and retained reports."
gh release upload $evidenceTag $archive
```

This transport does not publish the product or start certification. Duplicate create/upload fails;
inspect the existing submission instead of overwriting it. Draft reads require repository write
access. Code-scanning reads also require security-events access.

## Selection and recovery

Both release drivers verify this submission before pushing/tagging. Release independently checks
it before preparing the product draft or starting missing MSI/DEB certification. Collection checks
the complete release/asset inventory, exact candidate target, original asset ID/digest, bundle
manifest and freshness. It recomputes the diff against the current previous stable release and
rejects a changed predecessor or branch. Open CodeQL alerts of any severity, or an unavailable
alert inventory, block the gate. The branch is rechecked after reading alerts.

Release attaches and attests a verified copy, then recollects its origin and checks the actual
product attachment before publication. A changed, missing or expired submission fails; it cannot
turn another candidate's results into passing evidence. On unchanged inputs, inspect the retained
diagnostics and retry only failed jobs. Successful tests and package builds stay retained.

Manual pre-tag collection is available for review without tagging:

```powershell
node scripts/collect-operator-release-evidence.mjs etl-sql/ETL-SQL $candidateSha $version release-validation/operator-review
```

The output directory must be new. A failed report check can be inspected directly with
`scripts/Test-OperatorReportInputs.ps1 -Directory <bundle> -CandidateSha <sha> -Version <version>`;
this parses retained reports and does not rebuild or rerun their tests.

See the [release checklist](release-checklist.md) for the remaining candidate and publication gates.
