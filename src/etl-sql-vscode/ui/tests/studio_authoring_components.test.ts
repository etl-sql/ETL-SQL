/* eslint-disable @typescript-eslint/no-explicit-any */
import { describe, test, expect, beforeEach } from 'vitest';
import { JSDOM } from 'jsdom';

/**
 * The two shared authoring primitives the pipeline DAG is about to reuse.
 *
 * Both were built for one caller and hardened here before a second one arrives: `noteMarkup` used to
 * take trusted markup, so every caller had to remember to escape a server message, and the query
 * workbench's connection preamble used to be a regex that ended the statement at the first `;`.
 */
describe('Studio authoring presentation primitives', () => {
    let ui: any;

    beforeEach(async () => {
        ui = await import('../../media/designer/studio-authoring-ui.js');
    });

    test('noteMarkup escapes plain text, including a server message', () => {
        const markup = ui.noteMarkup('<img src=x onerror=alert(1)> & "quoted"', 'error');

        expect(markup).toContain('is-error');
        expect(markup).not.toContain('<img');
        expect(markup).toContain('&lt;img src=x onerror=alert(1)&gt; &amp; &quot;quoted&quot;');
    });

    test('noteMarkup renders emphasis only when a segment asks for it, and escapes inside it', () => {
        const markup = ui.noteMarkup([
            'Run ',
            { code: 'CREATE CONNECTION <alias>' },
            ' first, then ',
            { strong: 'save' },
            '.',
        ]);

        expect(markup).toContain('<code>CREATE CONNECTION &lt;alias&gt;</code>');
        expect(markup).toContain('<strong>save</strong>');
        expect(markup).toContain('Run ');
    });

    test('noteMarkup refuses an unrecognised segment rather than dropping it', () => {
        expect(() => ui.noteMarkup([{ html: '<b>hi</b>' }])).toThrow(/unsupported segment/);
    });

    test('the tone is escaped as well, so it cannot break out of the class attribute', () => {
        expect(ui.noteMarkup('ok', 'info" onload="x')).not.toContain('onload="x"');
    });
});

describe('Query workbench connection preamble', () => {
    let workbench: any;

    beforeEach(async () => {
        const dom = new JSDOM('<!DOCTYPE html><html><body></body></html>', { url: 'http://localhost:3000' });
        (globalThis as any).window = dom.window;
        (globalThis as any).document = dom.window.document;
        (globalThis as any).HTMLElement = dom.window.HTMLElement;
        (globalThis as any).customElements = dom.window.customElements;
        workbench = await import('../../media/designer/studio-query-workbench.js');
    });

    const routes = { parse: '/api/designer/parse' };
    const parseReturning = (connections: any[]) => async (_route: string, _init: any) =>
        ({ designState: { connections } });

    test('uses the declaration the parse route reports, terminated exactly once', async () => {
        const preamble = await workbench.connectionPreamble('sales', 'CREATE CONNECTION sales AS MOCKDB();', {
            routes,
            request: parseReturning([{ name: 'sales', text: 'CREATE CONNECTION sales AS MOCKDB()' }]),
        });

        expect(preamble).toBe('CREATE CONNECTION sales AS MOCKDB();\n');
    });

    test('keeps a multiline body whose options contain a semicolon inside a string', async () => {
        const declaration = "CREATE CONNECTION warehouse AS SQLSERVER(\n"
            + "    SERVER = 'db01;failover=db02',\n"
            + "    PASSWORD = 'p;w'\n"
            + ')';
        const preamble = await workbench.connectionPreamble('warehouse', `${declaration};`, {
            routes,
            request: parseReturning([{ name: 'warehouse', text: declaration }]),
        });

        // The regex this replaced stopped at the semicolon inside SERVER, producing an unparseable
        // preamble and a run that failed against a script that declares the alias perfectly well.
        expect(preamble).toBe(`${declaration};\n`);
    });

    test('matches the alias case-insensitively and ignores bracket quoting', async () => {
        const preamble = await workbench.connectionPreamble('[Sales]', 'CREATE CONNECTION sales AS MOCKDB();', {
            routes,
            request: parseReturning([{ name: 'sales', text: 'CREATE CONNECTION sales AS MOCKDB();' }]),
        });

        expect(preamble).toBe('CREATE CONNECTION sales AS MOCKDB();\n');
    });

    test('an alias the script does not declare contributes no preamble', async () => {
        const preamble = await workbench.connectionPreamble('missing', 'CREATE CONNECTION sales AS MOCKDB();', {
            routes,
            request: parseReturning([{ name: 'sales', text: 'CREATE CONNECTION sales AS MOCKDB();' }]),
        });

        expect(preamble).toBe('');
    });

    test('no connection, or an empty script, needs no parse call at all', async () => {
        const request = async () => { throw new Error('should not be called'); };
        expect(await workbench.connectionPreamble(null, 'anything', { routes, request })).toBe('');
        expect(await workbench.connectionPreamble('sales', '   ', { routes, request })).toBe('');
    });

    test('a script that does not parse fails loudly with the parse error', async () => {
        await expect(workbench.connectionPreamble('sales', 'CREATE CONNECTION', {
            routes,
            request: async () => ({ error: 'Syntax error: Unexpected token in script' }),
        })).rejects.toThrow(/Unexpected token in script/);

        await expect(workbench.connectionPreamble('sales', 'CREATE CONNECTION', {
            routes,
            request: async () => { throw new Error('The script could not be parsed.'); },
        })).rejects.toThrow(/could not be parsed/);
    });
});
