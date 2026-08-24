/* eslint-disable @typescript-eslint/no-explicit-any */
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


    // Prevent fetch from being called (not available in jsdom by default).
    win.fetch = () => Promise.reject(new Error('fetch not available in tests'));

    // jsdom has no `CSS` global, so CSS.escape — which the runtime uses to build attribute
    // selectors — throws a ReferenceError that real browsers never see. Shim it so tests exercise
    // the same code path the browser does rather than an accidental error branch.
    if (!win.CSS) { win.CSS = { escape: (value: string) => String(value).replace(/["\\]/g, (m: string) => '\\' + m) }; }

    if (extraSetup) { extraSetup(win); }

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

// ── Native SVG charts ─────────────────────────────────────────────────────────

describe("renderNativeSvg()", () => {
    it("mounts a native SVG payload and exposes row marks", () => {
        const win = makeDOM();
        const container = win.document.createElement("div");
        win.__reportRuntime__.renderNativeSvg(container, {
            name: "Sales", visualType: "BAR", columns: ["Category", "Value"], rows: [["A", "10"]], options: {}, actions: [],
            nativeSvg: "<svg xmlns=\"http://www.w3.org/2000/svg\"><rect data-row-index=\"0\" width=\"10\" height=\"10\"/></svg>"
        });
        expect(container.querySelector("svg")).not.toBeNull();
        expect(container.querySelector("[data-row-index=\"0\"]")).not.toBeNull();
    });
});

// ── Views picker (author bookmarks + Portal saved views) ──────────────────────

describe('savedViewsBase()', () => {
    it('addresses saved views off the Portal host base rather than rebuilding the path', () => {
        // The Portal hosts the runtime with __API_BASE__ = '/api/reports/{id}'. Concatenating
        // '/api/reports/{id}' onto that base again produced a URL that 404s silently.
        const win = makeDOM(w => { w.__IS_WEB__ = true; w.__API_BASE__ = '/api/reports/42'; });
        expect(win.__reportRuntime__.savedViewsBase()).toBe('/api/reports/42/saved-views');
    });

    it('is unavailable in the ReportPlayer, which has no per-user saved-view API', () => {
        const win = makeDOM(w => { w.__IS_WEB__ = true; w.__API_BASE__ = '/reports/Summary/api'; });
        expect(win.__reportRuntime__.savedViewsBase()).toBeNull();
    });

    it('is unavailable in the VS Code preview and in offline snapshots', () => {
        const vscodeWin = makeDOM(w => {
            w.acquireVsCodeApi = () => ({ postMessage: () => {} });
            w.__API_BASE__ = '/api/reports/42';
        });
        expect(vscodeWin.__reportRuntime__.savedViewsBase()).toBeNull();

        const offlineWin = makeDOM(w => { w.__API_BASE__ = '/api/reports/42'; w.__ETLSNAP__ = {}; });
        expect(offlineWin.__reportRuntime__.savedViewsBase()).toBeNull();
    });
});

describe('buildViewsPicker()', () => {
    it('returns nothing when there are no bookmarks and no saved-view API', () => {
        const win = makeDOM();
        expect(win.__reportRuntime__.buildViewsPicker({ ...EMPTY_MANIFEST, bookmarks: [] })).toBeNull();
    });

    it('exposes an accessible menu button wired to its menu', () => {
        const win = makeDOM();
        const picker = win.__reportRuntime__.buildViewsPicker({
            ...EMPTY_MANIFEST,
            bookmarks: [{ name: 'Overview', isDefault: true }, { name: 'Detail' }],
        });
        const button = picker.querySelector('button');
        const menu = picker.querySelector('[role="menu"]');

        expect(button.getAttribute('aria-haspopup')).toBe('menu');
        expect(button.getAttribute('aria-expanded')).toBe('false');
        expect(button.getAttribute('aria-controls')).toBe(menu.id);
        expect(menu.getAttribute('aria-labelledby')).toBe(button.id);
        expect(menu.hidden).toBe(true);
    });

    it('opens on click, focuses the first item, and closes on Escape restoring focus', async () => {
        const win = makeDOM();
        const picker = win.__reportRuntime__.buildViewsPicker({
            ...EMPTY_MANIFEST,
            bookmarks: [{ name: 'Overview' }, { name: 'Detail' }],
        });
        win.document.body.appendChild(picker);
        const button = picker.querySelector('button');
        const menu = picker.querySelector('[role="menu"]');

        button.click();
        await new Promise(resolve => setTimeout(resolve, 0));

        expect(menu.hidden).toBe(false);
        expect(button.getAttribute('aria-expanded')).toBe('true');
        const items = Array.from(menu.querySelectorAll('[role="menuitem"]')) as any[];
        expect(items.map(i => i.textContent)).toEqual(['Overview', 'Detail']);
        expect(win.document.activeElement).toBe(items[0]);

        // Arrow keys roam the menu; every item is removed from the tab order while closed.
        expect(items.every(i => i.tabIndex === -1)).toBe(true);
        menu.dispatchEvent(new win.KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }));
        expect(win.document.activeElement).toBe(items[1]);

        menu.dispatchEvent(new win.KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
        expect(menu.hidden).toBe(true);
        expect(button.getAttribute('aria-expanded')).toBe('false');
        expect(win.document.activeElement).toBe(button);
    });

    it('separates author bookmarks from the reader\'s own saved views', async () => {
        const win = makeDOM(w => {
            w.__IS_WEB__ = true;
            w.__API_BASE__ = '/api/reports/7';
            w.fetch = (url: string) => Promise.resolve({
                ok: true,
                status: 200,
                json: () => Promise.resolve(
                    url === '/api/reports/7/saved-views'
                        ? [{ id: 3, name: 'My West', isDefault: true }]
                        : {}),
            });
        });
        const picker = win.__reportRuntime__.buildViewsPicker({
            ...EMPTY_MANIFEST,
            bookmarks: [{ name: 'Overview' }],
        });
        win.document.body.appendChild(picker);
        picker.querySelector('button').click();
        await new Promise(resolve => setTimeout(resolve, 0));

        const groups = Array.from(picker.querySelectorAll('[role="group"]')) as any[];
        const labels = groups.map(g => g.getAttribute('aria-label'));
        expect(labels).toContain('Report bookmarks');
        expect(labels).toContain('My saved views');
        expect(labels).toContain('Saved view actions');

        // The personal default is announced as such rather than relying on the ★ glyph alone.
        const mine = groups.find(g => g.getAttribute('aria-label') === 'My West');
        expect(mine.querySelector('[role="menuitem"]').getAttribute('aria-label')).toBe('My West (your default)');

        const actions = groups.find(g => g.getAttribute('aria-label') === 'Saved view actions');
        expect(Array.from(actions.querySelectorAll('[role="menuitem"]')).map((b: any) => b.textContent))
            .toEqual(['Save current view as…', 'Save as my default view', 'Reset to report default']);
    });
});

// ── Identifier-only URL state ─────────────────────────────────────────────────

describe('parseStateHash()', () => {
    it('reads identifiers only and never carries values', () => {
        const win = makeDOM();
        const parse = win.__reportRuntime__.parseStateHash;
        expect(parse('#bookmark=Overview')).toEqual({ bookmark: 'Overview', view: null });
        expect(parse('#view=42')).toEqual({ bookmark: null, view: '42' });
        expect(parse('')).toEqual({ bookmark: null, view: null });
        // An explicit bookmark outranks a view in the same hash.
        expect(parse('#bookmark=Overview&view=42').view).toBeNull();
        expect(parse('#bookmark=%E0%A4%A')).toEqual({ bookmark: null, view: null });
    });
});

describe('Portal saved-view application', () => {
    it('uses the server-side atomic apply endpoint', async () => {
        const calls: Array<{ url: string; method?: string }> = [];
        const win = makeDOM(w => {
            w.__IS_WEB__ = true;
            w.__API_BASE__ = '/api/reports/7';
            w.__MANIFEST__ = { ...EMPTY_MANIFEST, parameters: {} };
            w.fetch = (url: string, init: any = {}) => {
                calls.push({ url, method: init.method });
                return Promise.resolve({
                    ok: true,
                    status: 200,
                    json: () => Promise.resolve({
                        ...EMPTY_MANIFEST,
                        parameters: { '@Limit': '25' },
                        appliedState: { parameters: { '@Limit': 25 }, visible: {}, collapsed: {} },
                    }),
                });
            };
        });
        win.document.dispatchEvent(new win.Event('DOMContentLoaded'));
        await new Promise(resolve => setTimeout(resolve, 0));

        expect(await win.__reportRuntime__.applySavedView(12)).toBe(true);
        expect(calls.some(c => c.url === '/api/reports/7/saved-views/12/apply' && c.method === 'POST')).toBe(true);
        expect(win.location.hash).toBe('#view=12');
    });

    it('lets the launch default satisfy a required parameter before prompting', async () => {
        const win = makeDOM(w => {
            w.__IS_WEB__ = true;
            w.__API_BASE__ = '/api/reports/7';
            w.__MANIFEST__ = {
                ...EMPTY_MANIFEST,
                parameters: { '@Region': '' },
                parameterMetadata: { '@Region': { name: '@Region', type: 'VARCHAR', isRequired: true } },
                bookmarks: [{ name: 'DefaultRegion', isDefault: true, state: { parameters: { '@Region': 'West' } } }],
            };
            w.fetch = (url: string, init: any = {}) => {
                if (url === '/api/reports/7/saved-views/default')
                    return Promise.resolve({ ok: true, status: 204, json: () => Promise.resolve(null) });
                if (url === '/api/reports/7/bookmark' && init.method === 'POST')
                    return Promise.resolve({
                        ok: true,
                        status: 200,
                        json: () => Promise.resolve({
                            ...EMPTY_MANIFEST,
                            parameters: { '@Region': 'West' },
                            parameterMetadata: { '@Region': { name: '@Region', type: 'VARCHAR', isRequired: true } },
                            bookmarks: w.__MANIFEST__.bookmarks,
                            appliedState: { parameters: { '@Region': 'West' }, visible: {}, collapsed: {} },
                        }),
                    });
                return Promise.reject(new Error('unexpected request: ' + url));
            };
        });

        win.document.dispatchEvent(new win.Event('DOMContentLoaded'));
        await new Promise(resolve => setTimeout(resolve, 10));
        expect(win.__CURRENT_MANIFEST__.parameters['@Region']).toBe('West');
        expect(win.document.querySelector('.required-params-modal')).toBeNull();
    });
});

// ── Offline snapshot bookmark replay ──────────────────────────────────────────

describe('offline snapshot bookmark replay', () => {
    function offlineWindow() {
        return makeDOM(w => {
            w.__IS_WEB__ = true;
            // The offline contract: a snapshot bootstrap sets __ETLSNAP__ and pre-embeds the manifest.
            w.__ETLSNAP__ = { capturedAt: '2026-08-23T00:00:00Z' };
            w.__MANIFEST__ = {
                title: 'Snapshot Report',
                visuals: [],
                pages: [
                    { name: 'Summary', visuals: [] },
                    { name: 'Detail', visuals: [] },
                ],
                buttons: [],
                navigations: [],
                parameters: { '@Region': 'North' },
                parameterMetadata: {
                    '@Region': { name: '@Region', type: 'VARCHAR' },
                    '@Limit': { name: '@Limit', type: 'INT' },
                },
                bookmarks: [
                    {
                        name: 'WestQ4',
                        title: 'West, Q4',
                        state: {
                            activePage: 'Detail',
                            parameters: { '@Region': 'West', '@Limit': 25 },
                            collapsed: {},
                            visible: { FilterPanel: false },
                        },
                    },
                ],
            };
            // Any network call at all is a failure: a snapshot on disk has no server.
            w.fetch = () => Promise.reject(new Error('offline snapshot must not call the network'));
        });
    }

    it('applies a bookmark from the manifest without touching the network', async () => {
        const win = offlineWindow();
        win.document.dispatchEvent(new win.Event('DOMContentLoaded'));
        await new Promise(resolve => setTimeout(resolve, 0));

        expect(win.__reportRuntime__.isOfflineSnapshot()).toBe(true);

        const applied = await win.__reportRuntime__.applyBookmark('WestQ4');
        expect(applied).toBe(true);

        // The bookmarked parameter values replay onto the snapshot in memory, so the slicers show the
        // state the author captured even though the frozen rows cannot change.
        expect(win.__CURRENT_MANIFEST__.parameters['@Region']).toBe('West');
        expect(win.__CURRENT_MANIFEST__.parameters['@Limit']).toBe('25');

        // Identifier only — never the values — in the URL.
        expect(win.location.hash).toBe('#bookmark=WestQ4');
    });

    it('reports an unknown bookmark rather than half-applying it', async () => {
        const win = offlineWindow();
        win.document.dispatchEvent(new win.Event('DOMContentLoaded'));
        await new Promise(resolve => setTimeout(resolve, 0));

        expect(await win.__reportRuntime__.applyBookmark('DoesNotExist')).toBe(false);
        expect(win.__CURRENT_MANIFEST__.parameters['@Region']).toBe('North');
        expect(win.location.hash).toBe('');
    });

    it('captures typed parameter and visibility state in the shared envelope', async () => {
        const win = offlineWindow();
        win.document.dispatchEvent(new win.Event('DOMContentLoaded'));
        await new Promise(resolve => setTimeout(resolve, 0));
        const panel = win.document.createElement('div');
        panel.setAttribute('data-name', 'FilterPanel');
        win.document.body.appendChild(panel);

        await win.__reportRuntime__.applyBookmark('WestQ4');
        const state = win.__reportRuntime__.captureResolvedState();
        expect(state.parameters['@Region']).toBe('West');
        expect(state.parameters['@Limit']).toBe(25);
        expect(state.visible.FilterPanel).toBe(false);
    });

    it('offers author bookmarks in the picker but no saved-view actions offline', async () => {
        const win = offlineWindow();
        const picker = win.__reportRuntime__.buildViewsPicker({
            visuals: [], pages: [], buttons: [], navigations: [],
            bookmarks: [{ name: 'WestQ4', title: 'West, Q4' }],
        });
        expect(picker).not.toBeNull();

        picker.querySelector('button').click();
        await new Promise(resolve => setTimeout(resolve, 0));

        const labels = Array.from(picker.querySelectorAll('[role="group"]'))
            .map((g: any) => g.getAttribute('aria-label'));
        expect(labels).toContain('Report bookmarks');
        // Saved views are a Portal feature; offering save-as in a file on disk would be a dead control.
        expect(labels).not.toContain('My saved views');
        expect(labels).not.toContain('Saved view actions');
    });
});
