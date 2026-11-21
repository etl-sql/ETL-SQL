// Renders the README report gallery (docs/assets/gallery/*.png) from runnable report recipes.
//
// Each image is the first or named ```sql block of a recipe in docs/cookbooks/report, built with
// `ETL-SQL-Report offline` into a self-contained HTML page and captured with headless Chromium.
// The recipes run as written in RunnableDocSampleTests, so the gallery only shows charts that
// a reader gets from pasting the recipe. Rerun after a visual or theme change.
//
// Usage: node scripts/update-readme-gallery.mjs <playwright-package> [report-cli]
//   playwright-package  e.g. tests/ETL-SQL.Portal.BrowserTests/bin/Debug/net10.0/.playwright/package
//   report-cli          ETL-SQL-Report executable (default: the Debug build of ETL-SQL.ReportBuilder.CLI)
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { createRequire } from 'node:module';
import { fileURLToPath, pathToFileURL } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const [playwrightArg, cliArg] = process.argv.slice(2);
if (!playwrightArg) {
    console.error('Usage: node scripts/update-readme-gallery.mjs <playwright-package> [report-cli]');
    process.exit(2);
}
const suffix = process.platform === 'win32' ? '.exe' : '';
const reportCli = path.resolve(cliArg ?? path.join(root, `src/ETL-SQL.ReportBuilder.CLI/bin/Debug/net10.0/ETL-SQL-Report${suffix}`));
if (!fs.existsSync(reportCli)) throw new Error(`Report CLI not found: ${reportCli}. Build src/ETL-SQL.ReportBuilder.CLI first.`);
const { chromium } = createRequire(import.meta.url)(path.resolve(playwrightArg));

/** name -> recipe file and the 1-based ```sql block to render. */
export const gallery = [
    { name: 'choropleth-map', recipe: 'choropleth-map-charts.md', block: 1 },
    { name: 'sankey-flow', recipe: 'alluvial-flow-composition.md', block: 1 },
    { name: 'waterfall-funnel-gauge', recipe: 'waterfall-funnel-gauge.md', block: 1 },
    { name: 'sales-forecast', recipe: 'sales-trend-with-forecasting.md', block: 1 }
];

function sqlBlock(markdown, index) {
    const blocks = [...markdown.replace(/\r\n/g, '\n').matchAll(/^```sql\n([\s\S]*?)^```/gm)].map(m => m[1]);
    if (!blocks[index - 1]) throw new Error(`Recipe has no sql block ${index}.`);
    return blocks[index - 1];
}

/** Tall dashboards are cut here; the gallery shows the top of the report. */
const maxHeight = 1000;
const work = fs.mkdtempSync(path.join(os.tmpdir(), 'readme-gallery-'));
const output = path.join(root, 'docs/assets/gallery');
fs.mkdirSync(output, { recursive: true });
const browser = await chromium.launch({ headless: true });
const failures = [];
try {
    for (const item of gallery) {
        const script = path.join(work, `${item.name}.rptsql`);
        const html = path.join(work, `${item.name}.html`);
        fs.writeFileSync(script, sqlBlock(fs.readFileSync(path.join(root, 'docs/cookbooks/report', item.recipe), 'utf8'), item.block));
        // `refresh` runs the script into a snapshot; `offline` turns the snapshot into one page.
        let built;
        for (const args of [['refresh', script], ['offline', script, '--output', html]]) {
            built = spawnSync(reportCli, args, { cwd: work, encoding: 'utf8' });
            if (built.status !== 0) break;
        }
        if (built.status !== 0 || !fs.existsSync(html)) {
            failures.push(`${item.name}: report CLI exited ${built.status}\n${built.stdout}\n${built.stderr}`);
            continue;
        }
        const page = await browser.newPage({ viewport: { width: 1280, height: maxHeight }, colorScheme: 'light' });
        const errors = [];
        page.on('pageerror', e => errors.push(e.message));
        page.on('request', r => { if (!r.url().startsWith('file:') && !r.url().startsWith('data:')) errors.push(`external request ${r.url()}`); });
        await page.goto(pathToFileURL(html).href, { waitUntil: 'load' });
        // The gallery shows the report, not the run: no owner/refresh badges (a recipe has no
        // owner), card toolbars or the pipeline console bar.
        await page.addStyleTag({ content: '.header-badges, .visual-toolbar, .pipeline-console { display: none !important; }' });
        await page.waitForTimeout(1500);
        if (errors.length) failures.push(`${item.name}: ${errors.join('; ')}`);
        const height = Math.min(await page.evaluate(() => document.documentElement.scrollHeight), maxHeight);
        await page.screenshot({ path: path.join(output, `${item.name}.png`), clip: { x: 0, y: 0, width: 1280, height } });
        await page.close();
        console.log(`rendered ${item.name}`);
    }
} finally {
    await browser.close();
    fs.rmSync(work, { recursive: true, force: true });
}
if (failures.length) {
    console.error(failures.join('\n'));
    process.exit(1);
}
