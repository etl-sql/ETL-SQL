/**
 * report-runtime.js unit tests
 *
 * Uses Node's `jsdom` package directly (not Vitest's jsdom environment) so the
 * existing `environment: 'node'` vitest config needs no changes.  Each test
 * spins up a fresh JSDOM instance, injects the script, and calls functions
 * through the `window.__reportRuntime__` test hook.
 */
import { describe, it, expect, beforeEach } from 'vitest';
import { JSDOM } from 'jsdom';
import { readFileSync } from 'fs';
import { resolve } from 'path';

const RUNTIME_PATH = resolve(
    __dirname,
    '../../../../src/ETL-SQL.ReportRuntime/Resources/Shared/report-runtime.js'
);
const RUNTIME_SRC = readFileSync(RUNTIME_PATH, 'utf8');

const EMPTY_MANIFEST = { visuals: [], pages: [], buttons: [], navigations: [] };

describe('VS Code preview header chrome', () => {
    it('does not inject legacy emoji action buttons', () => {
        expect(RUNTIME_SRC).not.toContain('&#x1F680;');
        expect(RUNTIME_SRC).not.toContain('&#x1F4DC;');
        expect(RUNTIME_SRC).not.toContain('&#x2133;');
        expect(RUNTIME_SRC).not.toContain('Launch into Browser');
        expect(RUNTIME_SRC).not.toContain('Publish to Markdown');
    });

    it('renders compact VS Code preview actions without legacy labels', async () => {
        const win = makeDOM(w => {
            w.acquireVsCodeApi = () => ({ postMessage: () => {} });
            w.__MANIFEST__ = {
                title: 'Preview Report',
                description: 'Rendered in VS Code',
                visuals: [],
                pages: [],
                buttons: [],
                navigations: [],
            };
        });

        win.document.dispatchEvent(new win.Event('DOMContentLoaded'));
        await new Promise(resolve => setTimeout(resolve, 0));

        expect(win.document.querySelector('.report-header')).not.toBeNull();
        const actions = win.document.querySelector('.header-actions');
        expect(actions).not.toBeNull();
        expect(Array.from(actions!.querySelectorAll('button')).map((b: any) => b.textContent)).toEqual(['Open', 'PDF', 'MD', 'Publish']);
        expect(win.document.body.textContent).not.toContain('Serve');
    });
});

/** Spin up a fresh DOM with the runtime loaded. */
function makeDOM(extraSetup?: (win: any) => void): any {
    const dom = new JSDOM(
        '<!DOCTYPE html><html><body><div id="root"></div></body></html>',
        { url: 'http://localhost/', runScripts: 'dangerously' }
    );
    const win = dom.window as any;

    // Minimal echarts stub so renderChart doesn't throw.
    win.echarts = {
        init: () => ({
            setOption: () => {},
            on: () => {},
            getOption: () => ({}),
        }),
    };

    // Prevent fetch from being called (not available in jsdom by default).
    win.fetch = () => Promise.reject(new Error('fetch not available in tests'));

    if (extraSetup) extraSetup(win);

    // Execute the script; __reportRuntime__ is attached to win.
    const script = dom.window.document.createElement('script');
    script.textContent = RUNTIME_SRC;
    dom.window.document.head.appendChild(script);

    return win;
}

// ── isOn ─────────────────────────────────────────────────────────────────────

describe('isOn()', () => {
    let win: any;
    beforeEach(() => { win = makeDOM(); });

    it('returns true for "ON"',   () => expect(win.__reportRuntime__.isOn('ON')).toBe(true));
    it('returns true for "on"',   () => expect(win.__reportRuntime__.isOn('on')).toBe(true));
    it('returns true for "TRUE"', () => expect(win.__reportRuntime__.isOn('TRUE')).toBe(true));
    it('returns true for "1"',    () => expect(win.__reportRuntime__.isOn('1')).toBe(true));
    it('returns false for "OFF"', () => expect(win.__reportRuntime__.isOn('OFF')).toBe(false));
    it('returns false for null',  () => expect(win.__reportRuntime__.isOn(null)).toBe(false));
    it('returns false for ""',    () => expect(win.__reportRuntime__.isOn('')).toBe(false));
});

// ── renderCard ───────────────────────────────────────────────────────────────

describe('renderCard()', () => {
    it('displays TITLE option, not visual name', () => {
        const win = makeDOM();
        const container = win.document.createElement('div');
        const visual = {
            name:       'KpiRevenue',
            visualType: 'CARD',
            options:    { title: 'Total Revenue' },
            columns:    ['Value'],
            rows:       [['1,234,567']],
            actions:    [],
        };
        win.__reportRuntime__.renderCard(container, visual);

        // The TITLE option should appear as the card label.
        expect(container.textContent).toContain('Total Revenue');
        // The raw visual name must NOT be used as the label.
        expect(container.querySelector('.card-label')?.textContent).not.toBe('KpiRevenue');
    });
});

// ── renderDatePicker ──────────────────────────────────────────────────────────

describe('renderDatePicker()', () => {
    it('binds parameterName from ON_CHANGE action (not OPTIONS.PARAMETER key)', () => {
        const win = makeDOM(w => { w.__IS_WEB__ = true; });
        const container = win.document.createElement('div');
        const visual = {
            name:       'StartDate',
            visualType: 'DATEPICKER',
            options:    { DEFAULT: '2024-01-01' },
            columns:    [],
            rows:       [],
            actions: [
                { trigger: 'ON_CHANGE', type: 'SET_PARAMETER', parameterName: '@startDate', columnRef: 'value' }
            ],
        };
        // Invoke with a bare manifest stub (slicers only need it for context)
        win.__reportRuntime__.renderDatePicker(container, visual, { visuals: [] });

        const input = container.querySelector('input[type=date]');
        expect(input).not.toBeNull();
        // Default value should be applied from OPTIONS.DEFAULT
        expect(input!.value).toBe('2024-01-01');
    });
});

// ── renderSlider ──────────────────────────────────────────────────────────────

describe('renderSlider()', () => {
    it('renders a range input with MIN/MAX/STEP from options', () => {
        const win = makeDOM(w => { w.__IS_WEB__ = true; });
        const container = win.document.createElement('div');
        const visual = {
            name:       'YearSlider',
            visualType: 'SLIDER',
            options:    { MIN: '2020', MAX: '2026', STEP: '1', DEFAULT: '2024' },
            columns:    [],
            rows:       [],
            actions: [
                { trigger: 'ON_CHANGE', type: 'SET_PARAMETER', parameterName: '@year', columnRef: 'value' }
            ],
        };
        win.__reportRuntime__.renderSlider(container, visual, { visuals: [] });

        const input = container.querySelector('input[type=range]');
        expect(input).not.toBeNull();
        expect(input!.min).toBe('2020');
        expect(input!.max).toBe('2026');
        expect(input!.step).toBe('1');
        expect(input!.value).toBe('2024');
    });
});

// ── renderSearch ──────────────────────────────────────────────────────────────

describe('renderSearch()', () => {
    it('renders a search input with PLACEHOLDER from options', () => {
        const win = makeDOM(w => { w.__IS_WEB__ = true; });
        const container = win.document.createElement('div');
        const visual = {
            name:       'ProductSearch',
            visualType: 'SEARCH',
            options:    { PLACEHOLDER: 'Type a product...', DEFAULT: '' },
            columns:    [],
            rows:       [],
            actions: [
                { trigger: 'ON_CHANGE', type: 'SET_PARAMETER', parameterName: '@searchTerm', columnRef: 'value' }
            ],
        };
        win.__reportRuntime__.renderSearch(container, visual, { visuals: [] });

        const input = container.querySelector('input[type=search]');
        expect(input).not.toBeNull();
        expect(input!.placeholder).toBe('Type a product...');
    });
});

// ── DRILL_IN ──────────────────────────────────────────────────────────────────

describe('DRILL_IN action', () => {
    /** Build a minimal chart visual with a DRILL_IN ON_CLICK action. */
    function drillVisual(overrides: any = {}) {
        return {
            name: 'DrillInByCategory',
            visualType: 'BAR',
            options: { 'mapping:x': 'Category' },
            columns: ['Category', 'Sales'],
            rows: [['Electronics', '50000'], ['Clothing', '30000'], ['Food', '20000']],
            chartConfig: { series: [{ type: 'bar', data: ['Electronics', 'Clothing', 'Food'] }] },
            actions: [{ trigger: 'ON_CLICK', type: 'DRILL_IN', hierarchy: ['Category', 'Region'] }],
            drillState: null,
            ...overrides,
        };
    }

    /** Make a DOM whose ECharts stub captures the click handler. */
    function makeDrillDOM(onFetch: (url: string, body: any) => any = () => null) {
        let clickHandler: ((params: any) => void) | null = null;
        const win = makeDOM(w => {
            w.__IS_WEB__ = true;
            w.__API_BASE__ = '/api/reports/2';
            w.echarts = {
                init: () => ({
                    setOption: () => {},
                    on: (event: string, handler: any) => { if (event === 'click') clickHandler = handler; },
                    getOption: () => ({}),
                }),
            };
            // boot() fetches /manifest on load because isWebMode=true in jsdom (http: protocol).
            // Return an empty manifest for that call; route everything else to onFetch.
            w.fetch = (url: string, init: any) => {
                if (url.endsWith('/manifest')) {
                    return Promise.resolve({ ok: true, json: () => Promise.resolve(EMPTY_MANIFEST) });
                }
                const body = init?.body ? JSON.parse(init.body) : null;
                const result = onFetch(url, body);
                return Promise.resolve({ ok: true, json: () => Promise.resolve(result) });
            };
        });
        return { win, getClickHandler: () => clickHandler };
    }

    it('POSTs to /drill with correct visualName and clickedValue on bar click', () => {
        const calls: { url: string; body: any }[] = [];
        const { win, getClickHandler } = makeDrillDOM((url, body) => { calls.push({ url, body }); return null; });

        const container = win.document.createElement('div');
        win.__reportRuntime__.renderChart(container, drillVisual(), null, null);

        const handler = getClickHandler();
        expect(handler).not.toBeNull();

        handler!({ dataIndex: 0, name: 'Electronics' });

        expect(calls).toHaveLength(1);
        expect(calls[0].url).toBe('/api/reports/2/drill');
        expect(calls[0].body).toEqual({ visualName: 'DrillInByCategory', direction: 'IN', clickedValue: 'Electronics' });
    });

    it('extracts clickedValue from visual.rows when params.name matches x-column', () => {
        const calls: { url: string; body: any }[] = [];
        const { win, getClickHandler } = makeDrillDOM((url, body) => { calls.push({ url, body }); return null; });

        const container = win.document.createElement('div');
        // Category is col[0]; clicking "Clothing" (row index 1)
        win.__reportRuntime__.renderChart(container, drillVisual(), null, null);

        getClickHandler()!({ dataIndex: 1, name: 'Clothing' });

        expect(calls[0].body.clickedValue).toBe('Clothing');
    });

    it('_drillInFlight guard blocks a second call while the first fetch is in flight', () => {
        let drillFetchCount = 0;
        let clickHandler: ((p: any) => void) | null = null;
        const win = makeDOM(w => {
            w.__IS_WEB__ = true;
            w.__API_BASE__ = '/api/reports/2';
            w.echarts = {
                init: () => ({
                    setOption: () => {},
                    on: (event: string, h: any) => { if (event === 'click') clickHandler = h; },
                    getOption: () => ({}),
                }),
            };
            w.fetch = (url: string) => {
                if (url.endsWith('/manifest')) {
                    return Promise.resolve({ ok: true, json: () => Promise.resolve(EMPTY_MANIFEST) });
                }
                // drill fetch never resolves — keeps _drillInFlight = true
                drillFetchCount++;
                return new Promise(() => {});
            };
        });

        const container = win.document.createElement('div');
        win.__reportRuntime__.renderChart(container, drillVisual(), null, null);

        clickHandler!({ dataIndex: 0, name: 'Electronics' });
        clickHandler!({ dataIndex: 0, name: 'Electronics' }); // duplicate

        expect(drillFetchCount).toBe(1);
    });
});

// ── Cross-filter / DRILL THROUGH HIGHLIGHT ────────────────────────────────────

describe('cross-filter (CROSS_FILTER = ON)', () => {
    /** A visual with cross-filter enabled and a well-defined x mapping. */
    function cfVisual(overrides: any = {}) {
        return {
            name: 'SalesByCategory',
            visualType: 'BAR',
            options: { 'mapping:x': 'Category', CROSS_FILTER: 'ON' },
            columns: ['Category', 'Sales'],
            rows: [['Electronics', '50000'], ['Clothing', '30000'], ['Food', '20000']],
            chartConfig: { series: [{ type: 'bar', data: ['Electronics', 'Clothing', 'Food'] }] },
            actions: [],
            ...overrides,
        };
    }

    /** Make a DOM with cross-filter wiring. Page structure is required for applyPageCrossFilter. */
    function makeCfDOM(onFetch: (url: string, body: any) => any = () => null) {
        let clickHandler: ((params: any) => void) | null = null;
        const win = makeDOM(w => {
            w.__IS_WEB__ = true;
            w.__API_BASE__ = '/api/reports/2';
            w.echarts = {
                init: () => ({
                    setOption: () => {},
                    on: (event: string, handler: any) => { if (event === 'click') clickHandler = handler; },
                    getOption: () => ({}),
                }),
            };
            w.fetch = (url: string, init: any) => {
                if (url.endsWith('/manifest')) {
                    return Promise.resolve({ ok: true, json: () => Promise.resolve(EMPTY_MANIFEST) });
                }
                const body = init?.body ? JSON.parse(init.body) : null;
                onFetch(url, body);
                return Promise.resolve({
                    ok: true,
                    json: () => Promise.resolve({ visuals: [], pages: [], buttons: [], navigations: [] }),
                });
            };
        });
        return { win, getClickHandler: () => clickHandler };
    }

    it('POSTs to /parameters with @Column=value and isInteraction:true on first click', () => {
        const calls: { url: string; body: any }[] = [];
        const { win, getClickHandler } = makeCfDOM((url, body) => calls.push({ url, body }));

        // applyPageCrossFilter looks up the pageEl via container.closest('.page')
        // so we need the visual inside a .page element inside root.
        const root    = win.document.getElementById('root');
        const pageEl  = win.document.createElement('div');
        pageEl.className = 'page';
        pageEl.id = 'page-sales';
        root.appendChild(pageEl);

        const container = win.document.createElement('div');
        container.className = 'visual-card';
        pageEl.appendChild(container);

        win.__reportRuntime__.renderChart(container, cfVisual(), null, null);

        const handler = getClickHandler();
        expect(handler).not.toBeNull();
        handler!({ dataIndex: 0, name: 'Electronics' });

        expect(calls).toHaveLength(1);
        expect(calls[0].url).toBe('/api/reports/2/parameters');
        expect(calls[0].body.isInteraction).toBe(true);
        const paramEntry = calls[0].body.params.find((p: any) => p.name === '@Category');
        expect(paramEntry).toBeDefined();
        expect(paramEntry.value).toBe('Electronics');
    });

    it('sends empty params array with isInteraction:false when toggling selection off', () => {
        const calls: { url: string; body: any }[] = [];
        const { win, getClickHandler } = makeCfDOM((url, body) => calls.push({ url, body }));

        const root    = win.document.getElementById('root');
        const pageEl  = win.document.createElement('div');
        pageEl.className = 'page';
        pageEl.id = 'page-sales2';
        root.appendChild(pageEl);

        const container = win.document.createElement('div');
        container.className = 'visual-card';
        pageEl.appendChild(container);

        win.__reportRuntime__.renderChart(container, cfVisual(), null, null);

        const handler = getClickHandler();
        handler!({ dataIndex: 0, name: 'Electronics' }); // select
        handler!({ dataIndex: 0, name: 'Electronics' }); // deselect (same bar again)

        // Second call should be the deselect: empty params, isInteraction:false
        const deselect = calls.find(c => c.body.params?.length === 0);
        expect(deselect).toBeDefined();
        expect(deselect!.body.isInteraction).toBe(false);
    });
});

// ── renderButton ──────────────────────────────────────────────────────────────

describe('renderButton()', () => {
    it('renders a <button> element for a REFRESH button', () => {
        const win = makeDOM();
        const container = win.document.createElement('div');
        const btn = {
            name:       'RefreshBtn',
            buttonType: 'REFRESH',
            label:      'Refresh',
            actions:    [],
        };
        win.__reportRuntime__.renderButton(container, btn);
        expect(container.querySelector('button')).not.toBeNull();
    });

    it('renders a <button> element for a custom ON_CLICK button', () => {
        const win = makeDOM();
        const container = win.document.createElement('div');
        const btn = {
            name:       'ExportBtn',
            buttonType: 'EXPORT',
            label:      'Export',
            actions: [
                { trigger: 'ON_CLICK', type: 'SET_PARAMETER', parameterName: '@export', columnRef: 'csv' }
            ],
        };
        win.__reportRuntime__.renderButton(container, btn);
        expect(container.querySelector('button')).not.toBeNull();
    });
});
