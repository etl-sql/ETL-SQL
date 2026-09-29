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
        ui = await import('../../../ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-authoring-ui.js');
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
        workbench = await import('../../../ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-query-workbench.js');
    });

    const routes = { parse: '/api/designer/parse' };
    const parseReturning = (connections: any[]) => async () =>
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

describe('Query workbench script composition and execution context', () => {
    let workbench: any;

    beforeEach(async () => {
        const dom = new JSDOM('<!DOCTYPE html><html><body></body></html>', { url: 'http://localhost:3000' });
        (globalThis as any).window = dom.window;
        (globalThis as any).document = dom.window.document;
        (globalThis as any).HTMLElement = dom.window.HTMLElement;
        (globalThis as any).customElements = dom.window.customElements;
        workbench = await import('../../../ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-query-workbench.js');
    });

    test('remote context wraps query in EXECUTE <conn> BEGIN ... END', () => {
        const script = workbench.composeWorkbenchScript('SELECT now()::date;', 'CREATE CONNECTION pg AS POSTGRES();\n', {
            connection: 'pg',
            context: 'remote',
        });

        expect(script).toBe(
            'CREATE CONNECTION pg AS POSTGRES();\n'
            + 'EXECUTE pg\n'
            + 'BEGIN\n'
            + '    SELECT now()::date\n'
            + 'END;'
        );
    });

    test('remote context indents multiline queries inside the block', () => {
        const query = 'SELECT 1 AS a,\n       2 AS b;\n';
        const script = workbench.composeWorkbenchScript(query, '', {
            connection: 'my_db',
            context: 'remote',
        });

        expect(script).toBe(
            'EXECUTE my_db\n'
            + 'BEGIN\n'
            + '    SELECT 1 AS a,\n'
            + '           2 AS b\n'
            + 'END;'
        );
    });

    test('engine context does not wrap query in remote execution', () => {
        const script = workbench.composeWorkbenchScript('SELECT * FROM my_conn.Customers', 'CREATE CONNECTION my_conn AS SQL();\n', {
            connection: 'my_conn',
            context: 'engine',
        });

        expect(script).toBe('CREATE CONNECTION my_conn AS SQL();\nSELECT * FROM my_conn.Customers;');
    });

    test('returns empty string when query is empty or whitespace', () => {
        expect(workbench.composeWorkbenchScript('', 'CREATE CONNECTION c AS SQL();')).toBe('');
        expect(workbench.composeWorkbenchScript('   ;   ', 'CREATE CONNECTION c AS SQL();')).toBe('');
    });
});

describe('Query workbench firstResultSet result extraction', () => {
    let workbench: any;

    beforeEach(async () => {
        const dom = new JSDOM('<!DOCTYPE html><html><body></body></html>', { url: 'http://localhost:3000' });
        (globalThis as any).window = dom.window;
        (globalThis as any).document = dom.window.document;
        (globalThis as any).HTMLElement = dom.window.HTMLElement;
        (globalThis as any).customElements = dom.window.customElements;
        workbench = await import('../../../ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-query-workbench.js');
    });

    test('treats a flat result with rows: [] as a valid zero-row result set', () => {
        const result = { columns: ['id', 'name'], rows: [], rowCount: 0 };
        const sample = workbench.firstResultSet(result);

        expect(sample).not.toBeNull();
        expect(sample.columns).toEqual(['id', 'name']);
        expect(sample.rows).toEqual([]);
        expect(sample.rowCount).toBe(0);
    });

    test('extracts trace resultset entry even when rows are empty', () => {
        const result = {
            trace: [{ type: 'resultset', data: { columns: ['count'], rows: [], rowCount: 0 } }],
        };
        const sample = workbench.firstResultSet(result);

        expect(sample).not.toBeNull();
        expect(sample.columns).toEqual(['count']);
        expect(sample.rows).toEqual([]);
        expect(sample.rowCount).toBe(0);
    });

    test('returns null when result has no rows array and no trace resultset', () => {
        expect(workbench.firstResultSet(null)).toBeNull();
        expect(workbench.firstResultSet({})).toBeNull();
        expect(workbench.firstResultSet({ trace: [{ type: 'log', message: 'done' }] })).toBeNull();
    });
});

describe('Query workbench interactive preview fidelity and educational empty results', () => {
    let workbench: any;

    beforeEach(async () => {
        const dom = new JSDOM('<!DOCTYPE html><html><body></body></html>', { url: 'http://localhost:3000' });
        (globalThis as any).window = dom.window;
        (globalThis as any).document = dom.window.document;
        (globalThis as any).HTMLElement = dom.window.HTMLElement;
        (globalThis as any).customElements = dom.window.customElements;
        workbench = await import('../../../ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-query-workbench.js');
    });

    const routes = {
        parse: '/api/designer/parse',
        run: '/api/designer/run',
    };

    test('native-dialect task executes through EXECUTE <conn> BEGIN ... END in remote context', async () => {
        const host = document.createElement('div');
        let executedScript = '';
        const request = async (route: string, options: any) => {
            if (route === routes.parse) {
                return { designState: { connections: [{ name: 'pg', text: 'CREATE CONNECTION pg AS POSTGRES();' }] } };
            }
            if (route === routes.run) {
                executedScript = options.body.script;
                return { columns: ['dt'], rows: [{ dt: '2026-09-17' }], rowCount: 1 };
            }
            return {};
        };

        const editor = await workbench.createQueryWorkbench(host, {
            connection: 'pg',
            context: 'remote',
            value: 'SELECT now()::date AS dt;',
            routes,
            request,
            scriptText: () => 'CREATE CONNECTION pg AS POSTGRES();',
        });

        const runButton = host.querySelector('[data-workbench-run]') as HTMLButtonElement;
        runButton.click();
        await new Promise(resolve => setTimeout(resolve, 10));

        expect(executedScript).toBe(
            'CREATE CONNECTION pg AS POSTGRES();\n'
            + 'EXECUTE pg\n'
            + 'BEGIN\n'
            + '    SELECT now()::date AS dt\n'
            + 'END;'
        );
        const output = host.querySelector('[data-workbench-output]')!;
        expect(output.innerHTML).toContain('2026-09-17');
        expect(output.innerHTML).toContain('1 row sampled');
        editor.dispose();
    });

    test('query over staged data does not silently execute predecessors and surfaces clean engine failure', async () => {
        const host = document.createElement('div');
        let executedScript = '';
        const request = async (route: string, options: any) => {
            if (route === routes.parse) {
                return { designState: { connections: [{ name: 'db', text: 'CREATE CONNECTION db AS SQL();' }] } };
            }
            if (route === routes.run) {
                executedScript = options.body.script;
                throw new Error("Table '#staged_data' does not exist.");
            }
            return {};
        };

        // The document contains preceding staging statements, but connectionPreamble carries only the connection
        const fullScript = 'CREATE CONNECTION db AS SQL();\nSELECT * INTO #staged_data FROM db.RawData;\n';
        const editor = await workbench.createQueryWorkbench(host, {
            connection: 'db',
            context: 'engine',
            value: 'SELECT * FROM #staged_data;',
            routes,
            request,
            scriptText: () => fullScript,
        });

        const runButton = host.querySelector('[data-workbench-run]') as HTMLButtonElement;
        runButton.click();
        await new Promise(resolve => setTimeout(resolve, 10));

        // Preamble only carried the CREATE CONNECTION, NOT the SELECT ... INTO #staged_data
        expect(executedScript).toBe('CREATE CONNECTION db AS SQL();\nSELECT * FROM #staged_data;');
        const output = host.querySelector('[data-workbench-output]')!;
        expect(output.innerHTML).toContain("Table '#staged_data' does not exist.");
        expect(output.innerHTML).toContain('is-error');
        editor.dispose();
    });

    test('query with required variables does not silently execute predecessors and surfaces clean engine failure', async () => {
        const host = document.createElement('div');
        let executedScript = '';
        const request = async (route: string, options: any) => {
            if (route === routes.parse) {
                return { designState: { connections: [{ name: 'db', text: 'CREATE CONNECTION db AS SQL();' }] } };
            }
            if (route === routes.run) {
                executedScript = options.body.script;
                throw new Error("Variable '@batch_id' must be declared.");
            }
            return {};
        };

        // The document contains preceding DECLARE statements, but connectionPreamble carries only the connection
        const fullScript = 'CREATE CONNECTION db AS SQL();\nDECLARE @batch_id = 42;\n';
        const editor = await workbench.createQueryWorkbench(host, {
            connection: 'db',
            context: 'engine',
            value: 'SELECT * FROM db.Batches WHERE id = @batch_id;',
            routes,
            request,
            scriptText: () => fullScript,
        });

        const runButton = host.querySelector('[data-workbench-run]') as HTMLButtonElement;
        runButton.click();
        await new Promise(resolve => setTimeout(resolve, 10));

        expect(executedScript).toBe('CREATE CONNECTION db AS SQL();\nSELECT * FROM db.Batches WHERE id = @batch_id;');
        const output = host.querySelector('[data-workbench-output]')!;
        expect(output.innerHTML).toContain("Variable '@batch_id' must be declared.");
        expect(output.innerHTML).toContain('is-error');
        editor.dispose();
    });

    test('valid zero-row result with columns renders grid with 0 rows sampled without reporting failure', async () => {
        const host = document.createElement('div');
        const request = async (route: string) => {
            if (route === routes.parse) {
                return { designState: { connections: [{ name: 'db', text: 'CREATE CONNECTION db AS SQL();' }] } };
            }
            if (route === routes.run) {
                return { columns: ['id', 'status'], rows: [], rowCount: 0 };
            }
            return {};
        };

        const editor = await workbench.createQueryWorkbench(host, {
            connection: 'db',
            context: 'engine',
            value: 'SELECT id, status FROM db.Items WHERE 1 = 0;',
            routes,
            request,
            scriptText: () => 'CREATE CONNECTION db AS SQL();',
        });

        const runButton = host.querySelector('[data-workbench-run]') as HTMLButtonElement;
        runButton.click();
        await new Promise(resolve => setTimeout(resolve, 10));

        const output = host.querySelector('[data-workbench-output]')!;
        expect(output.innerHTML).not.toContain('is-error');
        expect(output.innerHTML).not.toContain('The query ran but returned no result set.');
        expect(output.innerHTML).toContain('<th>id</th>');
        expect(output.innerHTML).toContain('<th>status</th>');
        expect(output.innerHTML).toContain('0 rows sampled · 2 fields');
        editor.dispose();
    });

    test('valid zero-row result with 0 columns renders educational info note', async () => {
        const host = document.createElement('div');
        const request = async (route: string) => {
            if (route === routes.parse) {
                return { designState: { connections: [{ name: 'db', text: 'CREATE CONNECTION db AS SQL();' }] } };
            }
            if (route === routes.run) {
                return { columns: [], rows: [], rowCount: 0 };
            }
            return {};
        };

        const editor = await workbench.createQueryWorkbench(host, {
            connection: 'db',
            context: 'engine',
            value: 'SELECT 1 WHERE 1 = 0;',
            routes,
            request,
            scriptText: () => 'CREATE CONNECTION db AS SQL();',
        });

        const runButton = host.querySelector('[data-workbench-run]') as HTMLButtonElement;
        runButton.click();
        await new Promise(resolve => setTimeout(resolve, 10));

        const output = host.querySelector('[data-workbench-output]')!;
        expect(output.innerHTML).not.toContain('is-error');
        expect(output.innerHTML).toContain('is-info');
        expect(output.innerHTML).toContain('The query ran successfully but returned 0 rows.');
        editor.dispose();
    });

    test('statement with no result set renders educational note instead of reporting error', async () => {
        const host = document.createElement('div');
        const request = async (route: string) => {
            if (route === routes.parse) {
                return { designState: { connections: [{ name: 'db', text: 'CREATE CONNECTION db AS SQL();' }] } };
            }
            if (route === routes.run) {
                return { rowCount: 0 };
            }
            return {};
        };

        const editor = await workbench.createQueryWorkbench(host, {
            connection: 'db',
            context: 'remote',
            value: 'UPDATE items SET processed = 1;',
            routes,
            request,
            scriptText: () => 'CREATE CONNECTION db AS SQL();',
        });

        const runButton = host.querySelector('[data-workbench-run]') as HTMLButtonElement;
        runButton.click();
        await new Promise(resolve => setTimeout(resolve, 10));

        const output = host.querySelector('[data-workbench-output]')!;
        expect(output.innerHTML).not.toContain('is-error');
        expect(output.innerHTML).toContain('is-info');
        expect(output.innerHTML).toContain('The query ran successfully but returned no result set.');
        editor.dispose();
    });
});
