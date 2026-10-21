// Regular CI selection. Full release certification always selects every lane.
import { spawnSync } from 'node:child_process';
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
export const manifest = JSON.parse(readFileSync(new URL('./ci-impact.json', import.meta.url), 'utf8'));

export function validateManifest(policy) {
  if (policy.schemaVersion !== 1 || !Array.isArray(policy.lanes) || !policy.lanes.length ||
      new Set(policy.lanes).size !== policy.lanes.length ||
      policy.lanes.some(lane => !/^[a-z][a-z0-9-]*$/.test(lane)) ||
      !Array.isArray(policy.always) || !Array.isArray(policy.rules) || !policy.rules.length) {
    throw new Error('Invalid CI impact manifest.');
  }
  const validLanes = lanes => Array.isArray(lanes) && lanes.every(lane => policy.lanes.includes(lane));
  if (!validLanes(policy.always)) throw new Error('Unknown mandatory lane.');
  for (const rule of policy.rules) {
    if (!rule.name || typeof rule.pattern !== 'string' || !rule.pattern.startsWith('^') ||
        (rule.lanes !== '*' && !validLanes(rule.lanes))) throw new Error('Invalid CI impact rule.');
    new RegExp(rule.pattern);
  }
}

export function selectLanes(paths, { full = false, fallbackReason = null, lanes, policy = manifest } = {}) {
  validateManifest(policy);
  if (!Array.isArray(paths) || paths.some(p => typeof p !== 'string' || !p ||
      p.includes('\\') || p.startsWith('/') || p.split('/').some(part => part === '..' || part === '.'))) {
    throw new Error('Expected repository-relative Git paths.');
  }
  const selected = new Set(policy.always);
  const reasons = [];
  if (lanes !== undefined) {
    if (full || !Array.isArray(lanes) || !lanes.length || lanes.some(lane => !policy.lanes.includes(lane) || lane === 'msi-upgrade')) {
      throw new Error('Diagnostic selection requires known lane names and cannot request full certification.');
    }
    lanes.forEach(lane => selected.add(lane));
    return { schemaVersion: 1, advisory: false, certification: false, selection: 'diagnostic',
      lanes: policy.lanes.filter(lane => selected.has(lane)), reasons: [{ path: null, rule: 'Explicit diagnostic selection', lanes }] };
  }
  if (full || fallbackReason) {
    policy.lanes.forEach(lane => selected.add(lane));
    reasons.push({ path: null, rule: fallbackReason ?? 'Explicit full selection', lanes: [...policy.lanes] });
  }
  for (const changedPath of [...new Set(paths)].sort()) {
    const rule = policy.rules.find(r => new RegExp(r.pattern).test(changedPath));
    const lanes = !rule || rule.lanes === '*' ? policy.lanes : rule.lanes;
    lanes.forEach(lane => selected.add(lane));
    reasons.push({ path: changedPath, rule: rule?.name ?? 'Unknown path: full selection', lanes });
  }
  return {
    schemaVersion: 1, advisory: false, certification: false,
    selection: full || fallbackReason || selected.size === policy.lanes.length ? 'full' : 'targeted',
    lanes: policy.lanes.filter(lane => selected.has(lane)), reasons,
  };
}

function git(cwd, args) {
  const result = spawnSync('git', args, { cwd, encoding: 'utf8', windowsHide: true, timeout: 30000, maxBuffer: 16 * 1024 * 1024 });
  if (result.error || result.status !== 0) throw new Error('Git comparison unavailable; selecting all lanes.');
  return result.stdout;
}

export function changedPaths({ cwd = root, base, head = 'HEAD', comparison = 'direct', workingTree = false } = {}) {
  if (!['direct', 'merge-base'].includes(comparison)) throw new Error('Invalid comparison mode.');
  try {
    const resolve = ref => {
      if (!ref || ref.startsWith('-') || /^0+$/.test(ref)) throw new Error('Missing comparison revision; selecting all lanes.');
      return git(cwd, ['rev-parse', '--verify', '--end-of-options', `${ref}^{commit}`]).trim();
    };
    const headSha = resolve(head);
    let baseSha = resolve(base);
    if (comparison === 'merge-base') baseSha = git(cwd, ['merge-base', baseSha, headSha]).trim();
    // Disable rename detection: the old deletion and new addition both participate in selection.
    const paths = git(cwd, ['diff', '--name-only', '-z', '--no-renames', baseSha, headSha, '--']).split('\0').filter(Boolean);
    if (workingTree) {
      paths.push(...git(cwd, ['diff', '--name-only', '-z', '--no-renames', headSha, '--']).split('\0').filter(Boolean));
      paths.push(...git(cwd, ['ls-files', '--others', '--exclude-standard', '-z']).split('\0').filter(Boolean));
    }
    return { paths: [...new Set(paths)].sort(), baseSha, headSha, comparison, workingTree };
  } catch (error) {
    return { paths: [], baseSha: null, headSha: null, comparison, workingTree, fallbackReason: error.message };
  }
}

export function runSelection(options) {
  const changes = changedPaths(options);
  return { ...selectLanes(changes.paths, { full: options.full, fallbackReason: changes.fallbackReason, lanes: options.lanes }), comparison: changes };
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const options = {};
    let output;
    const args = process.argv.slice(2);
    while (args.length) {
      const arg = args.shift();
      if (arg === '--full') options.full = true;
      else if (arg === '--working-tree') options.workingTree = true;
      else if (['--base', '--head', '--comparison', '--output', '--lanes'].includes(arg)) {
        const value = args.shift();
        if (!value || value.startsWith('--')) throw new Error(`Missing value for ${arg}.`);
        if (arg === '--output') output = value;
        else if (arg === '--lanes') options.lanes = value.split(',');
        else options[arg.slice(2)] = value;
      } else throw new Error(`Unknown option: ${arg}`);
    }
    const report = runSelection(options);
    if (output) {
      const destination = path.resolve(output);
      mkdirSync(path.dirname(destination), { recursive: true });
      writeFileSync(destination, JSON.stringify(report, null, 2) + '\n');
    }
    console.log(JSON.stringify(report, null, 2));
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
  }
}
