#!/usr/bin/env node
/**
 * Audits docs/syntax-index.md against the reference documentation tree.
 *
 * Two things must hold:
 *   1. Every reference/** link in the index resolves to a file that exists.
 *   2. Every reference page (excluding README.md) is reachable from the index.
 *
 * Deliberately NOT audited here: statement coverage derived from AST type names.
 * The CamelCase type name is not the surface syntax -- TryCatchStatement is written
 * `BEGIN TRY`, CreatePgpKeyPairStatement is `CREATE PGP_KEY_PAIR` -- so matching on
 * type names produces false gaps. Auditing that dimension needs syntax derived from the
 * parser's token dispatch, not from type names.
 *
 * Usage:
 *   node scripts/audit-syntax-index.js           # report only
 *   node scripts/audit-syntax-index.js --strict  # exit 1 if anything is wrong
 */
'use strict';

const fs   = require('fs');
const path = require('path');

const repoRoot     = path.resolve(__dirname, '..');
const indexPath    = path.join(repoRoot, 'docs', 'syntax-index.md');
const referencePath = path.join(repoRoot, 'docs', 'reference');

// Matches markdown links of the form ](reference/foo/bar.md)
// Anchors (#section) are intentionally excluded so the same file linked
// multiple times with different anchors is counted once.
const REFERENCE_LINK_RE = /\]\((reference\/[^)#]+\.md)\)/g;

function walkMarkdown(dir, results = []) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const fullPath = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      walkMarkdown(fullPath, results);
    } else if (
      entry.isFile() &&
      entry.name.endsWith('.md') &&
      entry.name.toLowerCase() !== 'readme.md'
    ) {
      results.push(path.normalize(fullPath));
    }
  }
  return results;
}

function audit() {
  const index  = fs.readFileSync(indexPath, 'utf8');
  const linked = new Set();
  const broken = [];

  let match;
  while ((match = REFERENCE_LINK_RE.exec(index)) !== null) {
    const link   = match[1];
    const target = path.normalize(path.join(repoRoot, 'docs', link));
    linked.add(target);
    if (!fs.existsSync(target)) {
      broken.push(link);
    }
  }

  const pages    = walkMarkdown(referencePath);
  const unlinked = pages
    .filter(p => !linked.has(p))
    .sort()
    .map(p => path.relative(repoRoot, p).replace(/\\/g, '/'));

  return { pageCount: pages.length, broken, unlinked };
}

function main() {
  const strict = process.argv.includes('--strict');
  const { pageCount, broken, unlinked } = audit();

  console.log('Syntax index audit');
  console.log(`  reference pages (excluding README): ${pageCount}`);
  console.log(`  broken links in index:              ${broken.length}`);
  console.log(`  pages not linked from index:        ${unlinked.length}`);

  if (broken.length > 0) {
    console.log('\nBroken links:');
    for (const link of broken) {
      console.log(`  ${link}`);
    }
  }

  if (unlinked.length > 0) {
    console.log('\nNot linked from syntax-index.md:');
    for (const p of unlinked) {
      console.log(`  ${p}`);
    }
  }

  if (strict && (broken.length > 0 || unlinked.length > 0)) {
    process.exitCode = 1;
  }
}

main();
