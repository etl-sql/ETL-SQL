import { mkdtemp, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { build } from 'vite';
import { expect, test } from 'vitest';
import { singleFileWebview } from '../singleFileWebview';

test('the built webview embeds JavaScript and CSS and preserves closing-tag string values', async () => {
    const root = await mkdtemp(join(tmpdir(), 'etlsql-webview-build-'));
    const runtime = globalThis as typeof globalThis & { __etlSqlBuildResult?: Promise<string> };
    const previousResult = runtime.__etlSqlBuildResult;
    try {
        await writeFile(join(root, 'index.html'),
            '<div id="root"></div><script type="module" src="./entry.js"></script>');
        await writeFile(join(root, 'entry.js'),
            'import "./style.css"; globalThis.__etlSqlBuildResult = import("./other.js").then(value => value.marker);');
        await writeFile(join(root, 'other.js'), 'export const marker = "</ScRiPt><!--marker";');
        await writeFile(join(root, 'style.css'), '.marker { color: red; }');
        const built = await build({
            root, configFile: false, logLevel: 'silent', base: './', plugins: [singleFileWebview()],
            build: {
                write: false, minify: false, cssCodeSplit: false, modulePreload: false,
                rollupOptions: { output: { codeSplitting: false } },
            },
        });
        if (Array.isArray(built) || !('output' in built)) throw new Error('Expected one webview output.');
        expect(built.output).toHaveLength(1);
        const html = built.output[0];
        if (html.type !== 'asset' || typeof html.source !== 'string') throw new Error('Expected inline HTML.');
        expect(html.fileName).toBe('index.html');
        expect(html.source).not.toMatch(/<script[^>]+src=|<link[^>]+rel="stylesheet"/);
        expect(html.source).toMatch(/<style[^>]*>[\s\S]*color: red/);
        const script = /<script[^>]*>([\s\S]*?)<\/script>/i.exec(html.source)?.[1];
        expect(script).toBeDefined();
        expect(script).not.toMatch(/<\/script|<!--/i);
        // Execute the emitted module: HTML escaping must preserve the original runtime string.
        await import(/* @vite-ignore */ `data:text/javascript;base64,${Buffer.from(script ?? '').toString('base64')}`);
        expect(await runtime.__etlSqlBuildResult).toBe('</ScRiPt><!--marker');
    } finally {
        if (previousResult === undefined) delete runtime.__etlSqlBuildResult;
        else runtime.__etlSqlBuildResult = previousResult;
        await rm(root, { recursive: true, force: true });
    }
});
