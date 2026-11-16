import { createHash } from 'node:crypto';
import { readFileSync, mkdirSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export function planBrowserShards(listing, count = 3) {
  if (!Number.isInteger(count) || count < 1 || count > 16) throw new Error('Shard count must be 1–16.');
  const lines = listing.replace(/^\uFEFF/, '').split(/\r?\n/);
  const marker = lines.findIndex(line => line.trim() === 'The following Tests are available:');
  if (marker < 0) throw new Error('Test discovery marker missing.');
  const names = lines.slice(marker + 1).map(line => line.trim()).filter(Boolean);
  if (!names.length || names.some(name => !/^ETL_SQL\.[\w.+]+\(/.test(name) && !/^ETL_SQL\.[\w.+]+$/.test(name))) {
    throw new Error('Empty or unrecognized browser discovery output.');
  }
  const classes = new Map();
  for (const name of names) {
    const method = name.split('(')[0];
    const className = method.slice(0, method.lastIndexOf('.'));
    if (!classes.has(className)) classes.set(className, { name: className, cases: 0, methods: new Set() });
    classes.get(className).cases++;
    classes.get(className).methods.add(method);
  }
  if (classes.size < count) throw new Error('Not enough test classes for nonempty shards.');
  // Ordinal sorting makes independent runners produce identical assignments.
  const compare = (a, b) => a < b ? -1 : a > b ? 1 : 0;
  const ordered = [...classes.values()].sort((a, b) => b.cases - a.cases || compare(a.name, b.name));
  const shards = Array.from({ length: count }, (_, index) => ({ index: index + 1, discoveredCases: 0, classes: [], methods: [] }));
  for (const entry of ordered) {
    const target = shards.reduce((best, next) => next.discoveredCases < best.discoveredCases ? next : best);
    target.classes.push(entry.name);
    target.methods.push(...entry.methods);
    target.discoveredCases += entry.cases;
  }
  for (const shard of shards) {
    shard.classes.sort(compare);
    shard.methods.sort(compare);
    shard.filter = `Category=Browser&(${shard.methods.map(method => `FullyQualifiedName=${method}`).join('|')})`;
  }
  // Display arguments can contain localized/truncated text or machine-specific paths. Bind
  // inventory to method identities and their multiplicity, which define these exact filters.
  const inventoryHash = createHash('sha256').update(JSON.stringify(names.map(name => name.split('(')[0]).sort(compare))).digest('hex');
  return { schemaVersion: 1, inventoryHash, discoveredCases: names.length, shardCount: count, shards };
}

export function shardSettings(shard) {
  const escaped = shard.filter.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;').replaceAll("'", '&apos;');
  return `<RunSettings><RunConfiguration><TestCaseFilter>${escaped}</TestCaseFilter></RunConfiguration></RunSettings>\n`;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    if (process.argv.length !== 5) throw new Error('Usage: node scripts/browser-shards.mjs <discovery.txt> <count> <output-directory>');
    const plan = planBrowserShards(readFileSync(process.argv[2], 'utf8'), Number(process.argv[3]));
    const output = path.resolve(process.argv[4]);
    mkdirSync(output, { recursive: true });
    writeFileSync(path.join(output, 'manifest.json'), JSON.stringify(plan, null, 2) + '\n');
    for (const shard of plan.shards) writeFileSync(path.join(output, `shard-${shard.index}.runsettings`), shardSettings(shard));
    console.log(`Discovered ${plan.discoveredCases} browser cases across ${plan.shardCount} shards; inventory ${plan.inventoryHash}.`);
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
  }
}
