// Exercises scripts/browser-asset-ownership.mjs against a throwaway Git repository: the tracked
// layout, the cutover, and each way a generated output or a required input can end up on the wrong
// side of the index. Nothing here reads or changes the real checkout.
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { ignoreEnd, ignoreStart, ownershipPlan, writeIgnoreRules } from '../browser-asset-ownership.mjs';
import { touchesBrowserAssets } from '../check-staged-browser-ownership.mjs';

const root = fs.mkdtempSync(path.join(os.tmpdir(), 'etlsql-ownership espaces-é-'));
const shared = 'src/ETL-SQL.ReportRuntime/Resources/Shared';
const owner = 'src/ETL-SQL.ReportRuntime/Resources/TypeScript/rt-util.ts';
const emitted = `${shared}/rt-util.js`;
const portalCopy = 'src/ETL-SQL.Portal/wwwroot/js/rt-util.js';
const playerCopy = 'src/ETL-SQL.ReportPlayer/wwwroot/rt-util.js';
const pageOwner = 'src/ETL-SQL.Portal/BrowserSources/pages/home [draft].ts';
const pageOutput = 'src/ETL-SQL.Portal/wwwroot/js/pages/home [draft].js';
const vendor = `${shared}/arrow.min.js`;
const page = 'src/ETL-SQL.Portal/wwwroot/index.html';
const project = 'src/ETL-SQL.Reporting/ETL-SQL.Reporting.csproj';

const write = (file, content) => {
    fs.mkdirSync(path.dirname(path.join(root, file)), { recursive: true });
    fs.writeFileSync(path.join(root, file), content);
};
const git = (...args) => {
    const result = spawnSync('git', args, { cwd: root, encoding: 'utf8' });
    assert.equal(result.status, 0, `git ${args.join(' ')}: ${result.stderr}`);
    return result.stdout;
};
const sha = content => createHash('sha256').update(content).digest('hex');
const outputs = { [emitted]: 'export const a = 1;\n', [portalCopy]: '// copy\nexport const a = 1;\n',
    [playerCopy]: '// copy\nexport const a = 1;\n', [pageOutput]: 'export {};\n' };
const inventory = () => write('artifacts/browser-assets.json', JSON.stringify({ version: 2, outputs: [
    { path: portalCopy, kind: 'host-copy', source: emitted, sha256: sha(outputs[portalCopy]) },
    { path: pageOutput, kind: 'typescript', source: pageOwner, sha256: sha(outputs[pageOutput]) },
    { path: playerCopy, kind: 'host-copy', source: emitted, sha256: sha(outputs[playerCopy]) },
    { path: emitted, kind: 'typescript', source: owner, sha256: sha(outputs[emitted]) },
] }, null, 2));
const problems = () => ownershipPlan(root).problems;
const expectProblem = (pattern, message) => assert.ok(problems().some(problem => pattern.test(problem)),
    `${message}\nReported: ${JSON.stringify(problems(), null, 2)}`);

// The pre-commit hook runs the check only for commits that can change the answer.
for (const file of [owner, emitted, portalCopy, playerCopy, page, 'types/etlsql.d.ts', '.gitignore',
    'scripts/lib/browser-asset-inputs.cjs', 'src/etl-sql-vscode/media/report-runtime.js', 'Directory.Build.targets'])
    assert.equal(touchesBrowserAssets([file]), true, `${file} must trigger the staged ownership check.`);
for (const file of ['src/ETL-SQL.Engine/Program.cs', 'docs/README.md', 'scripts/checks/test-dag.mjs', 'src/ETL-SQL.Portal/Program.cs', 'TODO.md'])
    assert.equal(touchesBrowserAssets([file]), false, `${file} must not trigger the staged ownership check.`);

try {
    git('init', '-q');
    git('config', 'user.email', 'ownership@example.invalid');
    git('config', 'user.name', 'Ownership Test');
    git('config', 'core.autocrlf', 'false');
    write('.gitignore', 'artifacts/\n');
    write(owner, 'export const a: number = 1;\n');
    write(pageOwner, 'export {};\n');
    write(vendor, '/* GENERATED FILE - DO NOT EDIT. */\nvar arrow;\n');
    write(page, '<link rel="stylesheet" href="/css/portal.css?v=3">\n<script type="module" src="/js/rt-util.js"></script>\n'
        + '<script type="module">import "./js/pages/home [draft].js"; import("/js/arrow.min.js");</script>\n'
        + '<script src="https://example.invalid/remote.js"></script>\n');
    write('src/ETL-SQL.Portal/wwwroot/css/portal.css', 'body{}\n');
    write('src/ETL-SQL.Portal/wwwroot/js/arrow.min.js', 'var arrow;\n');
    write(project, `<Project><ItemGroup><EmbeddedResource Include="..\\ETL-SQL.ReportRuntime\\Resources\\Shared\\rt-util.js" />`
        + `<EmbeddedResource Include="..\\ETL-SQL.ReportRuntime\\Resources\\Shared\\*.min.js" /></ItemGroup></Project>\n`);
    for (const [file, content] of Object.entries(outputs)) write(file, content);
    inventory();
    git('add', '-A');
    git('commit', '-q', '-m', 'tracked layout');

    // Tracked layout: every output is a proposed removal, every authored file a retained input.
    let plan = ownershipPlan(root);
    assert.deepEqual(plan.problems, [], 'A consistent tracked layout has nothing to report.');
    assert.equal(plan.cutover, false);
    assert.deepEqual(plan.removals.map(removal => removal.path).sort(), Object.keys(outputs).sort());
    assert.ok(plan.removals.every(removal => removal.regenerate === 'node scripts/sync-assets.js'));
    for (const input of [owner, pageOwner, vendor, page, 'src/ETL-SQL.Portal/wwwroot/css/portal.css'])
        assert.ok(plan.retained.includes(input), `${input} must be listed as a retained input.`);
    assert.ok(!plan.retained.includes(emitted), 'A generated output is never a retained input.');
    // A generated-looking banner in a vendor file does not make it an output.
    assert.ok(!plan.removals.some(removal => removal.path === vendor));

    // One anchored rule per output: no pattern can swallow an authored neighbour.
    const rules = plan.ignoreRules.split('\n');
    assert.equal(rules[0], ignoreStart);
    assert.equal(rules.at(-2), ignoreEnd);
    assert.deepEqual(rules.slice(1, -2), [...rules.slice(1, -2)].sort());
    assert.ok(rules.includes('/src/ETL-SQL.Portal/wwwroot/js/pages/home \\[draft\\].js'), 'Glob characters are escaped.');
    assert.ok(rules.slice(1, -2).every(rule => rule.startsWith('/') && !/(^|[^\\])[*?]/.test(rule)));

    // A staged edit to an output is reported against its owner, even with a clean working tree copy.
    write(emitted, 'export const a = 2;\n');
    git('add', emitted);
    write(emitted, outputs[emitted]);
    plan = ownershipPlan(root);
    assert.deepEqual(plan.modified, [emitted]);
    expectProblem(/Staged output differs.*rt-util\.js.*rt-util\.ts/, 'A staged output edit must name its owner.');
    git('add', emitted);

    // An output cannot leave the index, or be ignored, ahead of the cutover.
    git('rm', '-q', '--cached', playerCopy);
    expectProblem(/left the index before the cutover.*ReportPlayer/, 'A partial removal must be rejected.');
    git('add', playerCopy);
    write('.gitignore', 'artifacts/\n/src/ETL-SQL.ReportPlayer/wwwroot/\n');
    expectProblem(/ignored before the cutover.*ReportPlayer/, 'An early ignore rule must be rejected.');
    write('.gitignore', 'artifacts/\n');

    // A new owner that never reached the index would be missing from every clone.
    git('rm', '-q', '--cached', pageOwner);
    expectProblem(/Required browser input is not tracked: .*home \[draft\]\.ts/, 'An untracked owner must be rejected.');
    git('add', pageOwner);

    // Pages and project files name assets the import closure never visits.
    const validPage = fs.readFileSync(path.join(root, page), 'utf8');
    write(page, validPage + '<script type="module" src="/js/removed-module.js"></script>\n');
    git('add', page);
    expectProblem(/Consumer names an asset.*index\.html -> \/js\/removed-module\.js/, 'A page naming a missing module must be rejected.');
    write(page, validPage);
    git('add', page);
    const validProject = fs.readFileSync(path.join(root, project), 'utf8');
    write(project, validProject.replace('rt-util.js', 'rt-gone.js'));
    git('add', project);
    expectProblem(/Project includes an asset.*Reporting\.csproj -> .*rt-gone\.js/, 'A project embedding a missing asset must be rejected.');
    write(project, validProject);
    git('add', project);
    // A host that writes its script tags from C# names assets the same way a page does.
    const hostCode = 'src/ETL-SQL.ReportPlayer/Program.cs';
    write(hostCode, 'var html = "<script type=\\"module\\" src=\\"/rt-util.js\\"></script>";\n');
    git('add', hostCode);
    assert.deepEqual(problems(), [], 'A code reference to a generated host copy resolves.');
    write(hostCode, 'var html = "<script type=\\"module\\" src=\\"/rt-renamed.js\\"></script>";\n');
    git('add', hostCode);
    expectProblem(/Code names an asset.*Program\.cs -> \/rt-renamed\.js/, 'A code reference to a missing module must be rejected.');
    write(hostCode, 'var html = "<script type=\\"module\\" src=\\"/rt-util.js\\"></script>";\n');
    git('add', hostCode);
    assert.deepEqual(problems(), [], 'Restoring each input returns the layout to a clean state.');

    // Cutover: the block alone is not enough while outputs are still tracked.
    assert.equal(writeIgnoreRules(root), true);
    assert.equal(writeIgnoreRules(root), false, 'Writing current rules changes nothing.');
    assert.ok(fs.readFileSync(path.join(root, '.gitignore'), 'utf8').startsWith('artifacts/\n'), 'Other rules are preserved.');
    plan = ownershipPlan(root);
    assert.equal(plan.cutover, true);
    assert.equal(plan.problems.filter(problem => /Generated browser output is tracked/.test(problem)).length, 4);
    git('rm', '-q', '--cached', ...Object.keys(outputs));
    git('add', '.gitignore');
    plan = ownershipPlan(root);
    assert.deepEqual(plan.problems, [], 'The block plus the index removals is a complete cutover.');
    assert.deepEqual(plan.removals, [], 'Nothing is left to remove after the cutover.');
    for (const file of Object.keys(outputs)) assert.ok(fs.existsSync(path.join(root, file)), 'Prepared files stay on disk.');
    assert.equal(git('status', '--porcelain', '--untracked-files=all').split('\n').some(line => line.startsWith('??')), false,
        'Ignored outputs must not appear as untracked files.');

    // A force-added output is caught, and so is an ignore block that fell behind the inventory.
    git('add', '-f', portalCopy);
    expectProblem(/Generated browser output is tracked: .*wwwroot\/js\/rt-util\.js/, 'A re-tracked output must be rejected.');
    git('rm', '-q', '--cached', portalCopy);
    const newOutput = 'src/ETL-SQL.WorkstationEditor/wwwroot/rt-util.js';
    outputs[newOutput] = outputs[playerCopy];
    write(newOutput, outputs[newOutput]);
    const current = JSON.parse(fs.readFileSync(path.join(root, 'artifacts/browser-assets.json'), 'utf8'));
    current.outputs.push({ path: newOutput, kind: 'host-copy', source: emitted, sha256: sha(outputs[newOutput]) });
    write('artifacts/browser-assets.json', JSON.stringify(current, null, 2));
    expectProblem(/ignore rules are stale/, 'A newly generated output must invalidate the ignore block.');
    expectProblem(/not ignored: .*WorkstationEditor/, 'A newly generated output must be ignored before it can be committed.');
    assert.equal(writeIgnoreRules(root), true);
    assert.deepEqual(problems(), [], 'The checked update command brings the rules back in step.');

    // An output path that becomes authored again must leave the block before it can be tracked.
    const reduced = JSON.parse(fs.readFileSync(path.join(root, 'artifacts/browser-assets.json'), 'utf8'));
    reduced.outputs = reduced.outputs.filter(output => output.path !== emitted);
    write('artifacts/browser-assets.json', JSON.stringify(reduced, null, 2));
    expectProblem(/ignore rules are stale/, 'A removed output must invalidate the ignore block.');
    expectProblem(/Required browser input is ignored: .*Shared\/rt-util\.js/, 'A stale rule must not hide a file that is authored again.');
    assert.equal(writeIgnoreRules(root), true);
    expectProblem(/Required browser input is not tracked: .*Shared\/rt-util\.js/, 'The authored file must then enter the index.');
    git('add', emitted);
    assert.deepEqual(problems(), [], 'Once tracked, the authored file is an ordinary retained input.');

    // A broad rule that also hides an authored input is rejected.
    fs.appendFileSync(path.join(root, '.gitignore'), 'src/ETL-SQL.ReportRuntime/Resources/Shared/*.js\n');
    expectProblem(/Required browser input is ignored: .*arrow\.min\.js/, 'A rule hiding a vendor input must be rejected.');

    // A damaged block must not be read as "no cutover".
    write('.gitignore', `artifacts/\n${ignoreStart}\n`);
    assert.throws(() => ownershipPlan(root), /Malformed generated-output block/);
    fs.rmSync(path.join(root, 'artifacts/browser-assets.json'));
    assert.throws(() => ownershipPlan(root), /inventory is missing.*sync-assets/);
    console.log('browser asset ownership checks passed');
} finally {
    fs.rmSync(root, { recursive: true, force: true });
}
