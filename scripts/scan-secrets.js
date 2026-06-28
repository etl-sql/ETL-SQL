#!/usr/bin/env node
// scan-secrets.js — lightweight, dependency-free pre-release secret scan.
//
// Catches real credentials BEFORE they are pushed to the public repo (GitGuardian only fires
// post-push). Intentionally high-signal/low-noise: it flags only unambiguous secret formats
// (private keys, provider access tokens) that essentially never false-positive. It deliberately
// does NOT scan generic "password=" assignments — that class is pure noise in a codebase full of
// the word "password" and is GitGuardian's job. This is an early local tripwire, not a replacement
// for GitGuardian/CodeQL.
//
// Suppress a known-safe line with a trailing comment:  pragma: allowlist secret
// Intentional key/credential fixtures are listed in ALLOWLIST_PATHS below.
//
// Exit 0 = clean, 1 = potential secret(s) found.

const { execFileSync } = require('child_process');
const fs = require('fs');
const path = require('path');

const repoRoot = path.resolve(__dirname, '..');

// Files we never scan: vendored/minified/binary/lock artifacts (high noise, never hand-authored secrets).
const SKIP = [
  /(^|\/)node_modules\//,
  /\.min\.(js|css)$/,
  /\.map$/,
  /(^|\/)package-lock\.json$/,
  /\.(png|jpg|jpeg|gif|ico|svg|woff2?|ttf|eot|dll|exe|zip|vsix|deb|dmg|msi|parquet|etlsnap)$/i,
  /(^|\/)scripts\/scan-secrets\.js$/, // don't scan our own patterns
];

// Intentional credential fixtures (sample/test keys) — known non-production material.
// Add a path here only after confirming the credential is a throwaway test/sample value.
const ALLOWLIST_PATHS = new Set([
  'samples/10_Kitchen_Sinks/test_key/id_rsa',
  'tests/ETL-SQL.Tests/Connectors/BigQueryConnectorUnitTests.cs',
  'tests/ETL-SQL.Tests/Hardening/HardeningSshTests.cs',
  'tests/ETL-SQL.Tests/Hardening/SshKeyManagementTests.cs',
]);

// Unambiguous secret formats — these almost never false-positive in source.
const HARD_PATTERNS = [
  { name: 'Private key block', re: /-----BEGIN (?:RSA |EC |DSA |OPENSSH |PGP )?PRIVATE KEY-----/ },
  { name: 'AWS access key id', re: /\bAKIA[0-9A-Z]{16}\b/ },
  { name: 'GitHub token', re: /\bgh[pousr]_[A-Za-z0-9]{36,}\b/ },
  { name: 'Slack token', re: /\bxox[abprs]-[0-9A-Za-z-]{10,}\b/ },
  { name: 'Google API key', re: /\bAIza[0-9A-Za-z_-]{35}\b/ },
  { name: 'Stripe live key', re: /\b(?:sk|rk)_live_[0-9A-Za-z]{20,}\b/ },
  { name: 'Slack webhook', re: /https:\/\/hooks\.slack\.com\/services\/[A-Za-z0-9/]+/ },
];

// Well-known vendor documentation placeholders that match a pattern but are not real.
const DOC_PLACEHOLDERS = [/EXAMPLE/]; // e.g. AWS's AKIAIOSFODNN7EXAMPLE

function listTrackedFiles() {
  return execFileSync('git', ['ls-files'], { cwd: repoRoot, encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 })
    .split('\n')
    .map((s) => s.trim())
    .filter(Boolean)
    .filter((rel) => !ALLOWLIST_PATHS.has(rel))
    .filter((rel) => !SKIP.some((re) => re.test(rel)));
}

const findings = [];
for (const rel of listTrackedFiles()) {
  let text;
  try {
    const buf = fs.readFileSync(path.join(repoRoot, rel));
    if (buf.includes(0)) continue; // binary
    text = buf.toString('utf8');
  } catch {
    continue;
  }
  const lines = text.split('\n');
  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];
    if (/pragma:\s*allowlist secret/i.test(line)) continue;
    if (DOC_PLACEHOLDERS.some((re) => re.test(line))) continue;
    for (const { name, re } of HARD_PATTERNS) {
      if (re.test(line)) findings.push({ rel, line: i + 1, kind: name, text: line.trim().slice(0, 120) });
    }
  }
}

if (findings.length === 0) {
  console.log('Secret scan: clean (no high-signal secrets found in tracked files).');
  process.exit(0);
}

console.error(`Secret scan: ${findings.length} potential secret(s) found:`);
for (const f of findings) {
  console.error(`  ${f.rel}:${f.line}  [${f.kind}]  ${f.text}`);
}
console.error('\nIf a finding is a known-safe test/doc fixture, add the path to ALLOWLIST_PATHS in');
console.error("scripts/scan-secrets.js, or put 'pragma: allowlist secret' on the line. Otherwise");
console.error('remove the secret, rotate it, and purge it from history.');
process.exit(1);
