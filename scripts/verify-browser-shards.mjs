import { readFileSync, readdirSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export function verifyBrowserShards(records, count) {
  if (!Number.isInteger(count) || count < 1 || records.length !== count) throw new Error('Missing browser shard evidence.');
  const indexes = new Set();
  const methods = new Set();
  let inventory;
  let expectedMethods;
  for (const { manifest, receipt } of records) {
    if (manifest.schemaVersion !== 1 || receipt.schemaVersion !== 1 || receipt.status !== 'Passed' ||
        manifest.shardCount !== count || receipt.shardCount !== count ||
        !/^[a-f0-9]{64}$/.test(manifest.inventoryHash) ||
        receipt.inventoryHash !== manifest.inventoryHash ||
        !Number.isInteger(receipt.shard) || receipt.shard < 1 || receipt.shard > count ||
        indexes.has(receipt.shard)) throw new Error('Invalid or duplicate browser shard receipt.');
    indexes.add(receipt.shard);
    // Independent runners must agree on the complete partition, not only a claimed hash.
    const canonical = JSON.stringify(manifest);
    if (inventory && inventory !== canonical) throw new Error('Browser inventories differ across runners.');
    inventory = canonical;
    const selected = manifest.shards.filter(shard => shard.index === receipt.shard);
    if (selected.length !== 1 || !Array.isArray(receipt.methods) ||
        JSON.stringify([...receipt.methods].sort()) !== JSON.stringify([...selected[0].methods].sort()) ||
        !Number.isInteger(receipt.actualCases) || receipt.actualCases < selected[0].discoveredCases) {
      throw new Error('Browser shard result coverage is incomplete.');
    }
    expectedMethods = manifest.shards.flatMap(shard => shard.methods);
    for (const method of receipt.methods) {
      if (methods.has(method)) throw new Error('Browser test ran in multiple shards.');
      methods.add(method);
    }
  }
  if (expectedMethods.length !== methods.size || expectedMethods.some(method => !methods.has(method))) {
    throw new Error('Browser shard union differs from discovery.');
  }
  return { status: 'Passed', shardCount: count, methods: methods.size };
}

export function readBrowserShardArtifacts(directory, runId, count) {
  if (!/^[1-9][0-9]*$/.test(runId)) throw new Error('Invalid run ID.');
  const latest = new Map();
  for (const name of readdirSync(directory)) {
    const match = /^browser-shard-(\d+)-(\d+)-(\d+)$/.exec(name);
    if (!match || match[2] !== runId) throw new Error('Unexpected browser evidence directory.');
    const shard = Number(match[1]), attempt = Number(match[3]);
    if (shard < 1 || shard > count || attempt < 1) throw new Error('Invalid shard/attempt.');
    if (!latest.has(shard) || latest.get(shard).attempt < attempt) latest.set(shard, { name, attempt });
  }
  // Never fall back to a successful older receipt when the latest attempt is incomplete.
  return [...latest.entries()].map(([shard, item]) => {
    const load = name => JSON.parse(readFileSync(path.join(directory, item.name, name), 'utf8').replace(/^\uFEFF/, ''));
    const receipt = load('receipt.json');
    if (receipt.shard !== shard) throw new Error('Receipt shard does not match artifact identity.');
    return { manifest: load('manifest.json'), receipt };
  });
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    if (process.argv.length !== 5) throw new Error('Usage: node scripts/verify-browser-shards.mjs <artifact-directory> <run-id> <count>');
    console.log(JSON.stringify(verifyBrowserShards(readBrowserShardArtifacts(process.argv[2], process.argv[3], Number(process.argv[4])), Number(process.argv[4]))));
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}
