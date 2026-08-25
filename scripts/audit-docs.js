#!/usr/bin/env node
/**
 * audit-docs.js — Repository-wide documentation audit
 *
 * Checks that are NOT covered by audit-syntax-index.js:
 *
 *   1. Broken local Markdown links — every ](...md) or ](#anchor) target in
 *      docs/ must resolve to a file (and anchor) that exists.
 *   2. Filename/title policy — docs/architecture/** and prose docs must use
 *      lowercase-kebab-case; README.md and INDEX.md are the only exceptions.
 *      SQL-name files (function/statement/connector filenames that contain
 *      underscores or @@ prefixes) are exempt per the documented naming rule.
 *   3. Hub membership — every .md file inside a directory that has a README.md
 *      must be linked from that README (excluding the README itself).
 *   4. Template conformance — reference pages under docs/reference/ must contain
 *      the required sections for their type:
 *        functions/**  → ## Syntax, ## Returns, ## Example
 *        statements/** → ## Syntax, ## Example, ## References
 *        connectors/** → ## Syntax, ## Authentication, ## Examples, ## Troubleshooting
 *        visuals-reporting/visuals/** → ## Syntax, ## Mappings, ## Options, ## Example
 *
 * Usage:
 *   node scripts/audit-docs.js             # report only
 *   node scripts/audit-docs.js --strict    # exit 1 if any check fails
 *   node scripts/audit-docs.js --verbose   # show all issues even when passing
 */
'use strict';

const fs   = require('fs');
const path = require('path');

const repoRoot = path.resolve(__dirname, '..');
const docsDir  = path.join(repoRoot, 'docs');

const strict  = process.argv.includes('--strict');
const verbose = process.argv.includes('--verbose');
const LIMIT   = verbose ? Number.MAX_SAFE_INTEGER : 40;

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function walkDir(dir, results = []) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      walkDir(full, results);
    } else if (entry.isFile()) {
      results.push(full);
    }
  }
  return results;
}

function readFile(filePath) {
  try { return fs.readFileSync(filePath, 'utf8'); }
  catch { return null; }
}

/** Extract all anchors (#heading-text) from a markdown file. */
function extractAnchors(content) {
  const anchors = new Set();

  // Explicit anchors the docs already use and that a heading slug cannot produce:
  // the `{#custom-id}` heading attribute, and bare <a name>/<a id> targets.
  for (const m of content.matchAll(/\{#([\w-]+)\}/g)) anchors.add(m[1]);
  for (const m of content.matchAll(/<a\s+(?:name|id)=["']([^"']+)["']/gi)) anchors.add(m[1]);

  for (const line of content.split(/\r?\n/)) {
    const m = line.match(/^#{1,6}\s+(.+)$/);
    if (m) {
      // GitHub-style anchor: lowercase, strip punctuation, then map EVERY remaining
      // space to one dash. GitHub does not collapse runs, so "Backup & Restore" is
      // `backup--restore` — the removed `&` leaves the two spaces around it behind.
      // Collapsing them here under-generated anchors and reported live links as broken.
      const anchor = m[1]
        .replace(/\s*\{#[\w-]+\}\s*$/, '')   // a heading's explicit id is not part of its slug
        .toLowerCase()
        .replace(/[`*[\]()]/g, '')      // strip markdown emphasis/link chars
        .trim()
        .replace(/[^\w\s-]/g, '')       // strip other punctuation, keep the spaces
        .replace(/\s/g, '-');
      anchors.add(anchor);
    }
  }
  return anchors;
}

/** Parse all ]( link targets from markdown content. */
function extractLinks(content) {
  const links = [];
  const re = /\]\(([^)]+)\)/g;
  let m;
  while ((m = re.exec(content)) !== null) {
    links.push(m[1].trim());
  }
  return links;
}

/** Relative file path from repo root, forward slashes. */
function rel(filePath) {
  return path.relative(repoRoot, filePath).replace(/\\/g, '/');
}

// ---------------------------------------------------------------------------
// Check 1 — Broken local links and anchors
// ---------------------------------------------------------------------------
function checkBrokenLinks(mdFiles) {
  const issues = [];

  // Pre-build anchor map: filePath → Set<anchor>
  const anchorMap = new Map();
  for (const file of mdFiles) {
    const content = readFile(file);
    if (content) anchorMap.set(file, extractAnchors(content));
  }

  for (const file of mdFiles) {
    const content = readFile(file);
    if (!content) continue;

    for (const rawLink of extractLinks(content)) {
      // Only check local links (skip http/https/mailto and empty)
      if (!rawLink || rawLink.startsWith('http') || rawLink.startsWith('mailto:') ||
          rawLink.startsWith('conversation://')) continue;

      const [href, anchor] = rawLink.split('#');
      const dir   = path.dirname(file);

      // Pure anchor reference (#heading) — check within this file
      if (!href) {
        if (anchor) {
          const fileAnchors = anchorMap.get(file) || new Set();
          if (!fileAnchors.has(anchor)) {
            issues.push(`${rel(file)}: broken anchor #${anchor}`);
          }
        }
        continue;
      }

      // Ignore non-markdown and non-file references
      if (!href.endsWith('.md') && !href.includes('.')) continue;

      const target = path.resolve(dir, href);
      if (!fs.existsSync(target)) {
        issues.push(`${rel(file)}: broken link → ${href}`);
        continue;
      }

      // A fragment only means a heading anchor in Markdown. `Foo.cs#L42` is a source
      // line reference, and nothing in a .cs file can satisfy an anchor lookup.
      if (anchor && href.endsWith('.md')) {
        // Anchors are pre-built for docs/**; a link may legitimately point at a
        // Markdown file outside it (AGENTS.md, CONTRIBUTING.md), so read those on demand.
        if (!anchorMap.has(target)) {
          const targetContent = readFile(target);
          anchorMap.set(target, targetContent ? extractAnchors(targetContent) : new Set());
        }
        if (!anchorMap.get(target).has(anchor)) {
          issues.push(`${rel(file)}: broken anchor ${href}#${anchor}`);
        }
      }
    }
  }

  return issues;
}

// ---------------------------------------------------------------------------
// Check 2 — Filename/title policy
// ---------------------------------------------------------------------------
// Exempt patterns:
//   - README.md, INDEX.md (uppercase intentional)
//   - docs/reference/** where the filename contains _ or @@ (SQL-name convention)
//   - docs/releases/** version files (v0.x.y.md — dots are version separators)
//   - numbered ADR files (001-something.md — already kebab)
const SQL_NAME_RE  = /^(@@|.*_.*)/;     // contains @@ prefix or underscore
const VERSION_RE   = /^v\d+\.\d+/;      // version file
const KEBAB_RE     = /^[a-z0-9][a-z0-9\-]*\.md$/;

function isSqlNameExempt(filePath) {
  const relPath = rel(filePath);
  return relPath.startsWith('docs/reference/') && SQL_NAME_RE.test(path.basename(filePath));
}

function isVersionExempt(filePath) {
  return VERSION_RE.test(path.basename(filePath));
}

function checkFilenamePolicy(mdFiles) {
  const issues = [];
  // README.md and INDEX.md are canonical uppercased hub files.
  // QUICKSTART.md and TEMPLATE.md are entry-point/meta files whose uppercase
  // is intentional and not subject to the kebab-case rule.
  const skip   = new Set(['README.md', 'INDEX.md', 'QUICKSTART.md', 'TEMPLATE.md']);

  for (const file of mdFiles) {
    const name = path.basename(file);
    if (skip.has(name)) continue;
    if (isSqlNameExempt(file)) continue;
    if (isVersionExempt(file)) continue;
    if (!KEBAB_RE.test(name)) {
      issues.push(`${rel(file)}: filename not lowercase-kebab-case`);
    }
  }

  return issues;
}

// ---------------------------------------------------------------------------
// Check 3 — Hub membership
// ---------------------------------------------------------------------------
function checkHubMembership(mdFiles) {
  const issues = [];

  // Group files by directory
  const byDir = new Map();
  for (const file of mdFiles) {
    const dir = path.dirname(file);
    if (!byDir.has(dir)) byDir.set(dir, []);
    byDir.get(dir).push(file);
  }

  for (const [dir, files] of byDir) {
    const readmePath = path.join(dir, 'README.md');
    if (!fs.existsSync(readmePath)) continue;

    const readmeContent = readFile(readmePath) || '';

    for (const file of files) {
      const name = path.basename(file);
      if (name === 'README.md' || name === 'INDEX.md') continue;

      // Check the README links to this file by basename (relative link)
      if (!readmeContent.includes(`(${name})`)) {
        issues.push(`${rel(file)}: not linked from ${rel(readmePath)}`);
      }
    }
  }

  return issues;
}

// ---------------------------------------------------------------------------
// Check 4 — Template conformance by reference type
// ---------------------------------------------------------------------------
// Each rule's `required` entries are matched as case-insensitive prefixes so that
// "## Example" matches both "## Example" and "## Examples", and so on.
// Add `aliases` for headings that are semantically equivalent to the required one.
const CONFORMANCE_RULES = [
  {
    label: 'functions',
    match: f => rel(f).startsWith('docs/reference/functions/') && !rel(f).endsWith('/README.md'),
    required: [
      { key: '## syntax',     aliases: [] },
      { key: '## returns',    aliases: ['## return type', '## return'] },
      { key: '## example',    aliases: ['## usage', '## examples'] },
    ],
  },
  {
    label: 'statements',
    match: f => rel(f).startsWith('docs/reference/statements/') && !rel(f).endsWith('/README.md'),
    required: [
      { key: '## syntax',     aliases: ['## syntax and'] },
      { key: '## example',    aliases: ['## usage'] },
      { key: '## references', aliases: ['## see also', '## related'] },
    ],
  },
  {
    label: 'connectors',
    match: f => rel(f).startsWith('docs/reference/connectors/') && !rel(f).endsWith('/README.md'),
    required: [
      { key: '## syntax',         aliases: ['## connection syntax', '## create connection', '## connection string', '## options'] },
      { key: '## authentication', aliases: ['## authentication patterns', '## auth', '## credentials'] },
      { key: '## example',        aliases: ['## usage'] },
      { key: '## troubleshooting', aliases: ['## common issues', '## errors'] },
    ],
  },
  {
    label: 'visuals',
    match: f => rel(f).startsWith('docs/reference/visuals-reporting/visuals/') && !rel(f).endsWith('/README.md') && !rel(f).endsWith('/index.md'),
    required: [
      { key: '## syntax',   aliases: ['## create visual'] },
      { key: '## mappings', aliases: ['## mapping', '## columns', '## fields'] },
      { key: '## options',  aliases: ['## option', '## configuration'] },
      { key: '## example',  aliases: ['## usage'] },
    ],
  },
];

function headingMatches(line, entry) {
  const heading = line.trim().match(/^(#{2,4})\s+(.*)$/);
  if (!heading) return false;
  // The required section has to exist; which level the page nests it at is the page's
  // business. Visual and statement pages carry Mappings/Options/Example at H3 under a
  // single H2, and matching on "## " alone reported all of them as missing.
  const text = heading[2].toLowerCase().replace(/[`*]/g, '').trim();
  const wanted = [entry.key, ...entry.aliases].map(k => k.replace(/^#+\s*/, ''));
  // Prefix match handles plurals and qualifiers: "example" matches "examples" and
  // "example: daily reconciliation".
  return wanted.some(k => text.startsWith(k));
}

function checkTemplateConformance(mdFiles) {
  const summary = [];
  const allIssues = [];

  for (const rule of CONFORMANCE_RULES) {
    const matching = mdFiles.filter(rule.match);
    const missingBySection = new Map();

    for (const file of matching) {
      const content = readFile(file) || '';
      const contentLines = content.split(/\r?\n/);
      for (const entry of rule.required) {
        const found = contentLines.some(line => headingMatches(line, entry));
        if (!found) {
          if (!missingBySection.has(entry.key)) missingBySection.set(entry.key, []);
          missingBySection.get(entry.key).push(file);
          allIssues.push(`${rel(file)}: [${rule.label}] missing "${entry.key}"`);
        }
      }
    }

    const missing = [];
    for (const [key, files] of missingBySection) {
      missing.push({ section: key, count: files.length });
    }
    if (missing.length > 0 || verbose) {
      summary.push({ label: rule.label, total: matching.length, missing });
    }
  }

  return { allIssues, summary };
}

// ---------------------------------------------------------------------------
// Main
// ---------------------------------------------------------------------------
function main() {
  const allMd = walkDir(docsDir).filter(f => f.endsWith('.md'));

  console.log('Documentation audit');
  console.log(`  docs/ markdown files: ${allMd.length}`);
  console.log('');

  let failed = false;

  // --- Check 1: broken links ---
  const linkIssues = checkBrokenLinks(allMd);
  console.log(`[1] Broken local links and anchors: ${linkIssues.length} issue(s)`);
  if (linkIssues.length > 0) {
    failed = true;
    for (const issue of linkIssues.slice(0, LIMIT)) console.log(`    ${issue}`);
    if (linkIssues.length > LIMIT) console.log(`    ... and ${linkIssues.length - LIMIT} more`);
  }

  // --- Check 2: filename policy ---
  const nameIssues = checkFilenamePolicy(allMd);
  console.log(`[2] Filename policy violations:      ${nameIssues.length} issue(s)`);
  if (nameIssues.length > 0) {
    failed = true;
    for (const issue of nameIssues.slice(0, 40)) console.log(`    ${issue}`);
    if (nameIssues.length > 40) console.log(`    ... and ${nameIssues.length - 40} more`);
  }

  // --- Check 3: hub membership ---
  const hubIssues = checkHubMembership(allMd);
  console.log(`[3] Hub membership gaps:             ${hubIssues.length} issue(s)`);
  if (hubIssues.length > 0) {
    failed = true;
    for (const issue of hubIssues.slice(0, 40)) console.log(`    ${issue}`);
    if (hubIssues.length > 40) console.log(`    ... and ${hubIssues.length - 40} more`);
  }

  // --- Check 4: template conformance ---
  const { allIssues: confIssues, summary: confSummary } = checkTemplateConformance(allMd);
  console.log(`[4] Template conformance gaps:       ${confIssues.length} issue(s)`);
  for (const { label, total, missing } of confSummary) {
    if (missing.length > 0) {
      console.log(`    ${label} (${total} pages):`);
      for (const { section, count } of missing) {
        console.log(`      ${count} page(s) missing "${section}"`);
      }
    }
  }
  if (confIssues.length > 0) {
    failed = true;
    if (verbose) {
      for (const issue of confIssues.slice(0, LIMIT)) console.log(`    ${issue}`);
      if (confIssues.length > LIMIT) console.log(`    ... and ${confIssues.length - LIMIT} more`);
    }
  }

  console.log('');
  if (strict && failed) {
    console.log('Docs audit FAILED (--strict). Fix the issues above before pushing.');
    process.exitCode = 1;
  } else if (failed) {
    console.log('Docs audit found issues (run with --strict to gate on them).');
  } else {
    console.log('Docs audit passed.');
  }
}

main();
