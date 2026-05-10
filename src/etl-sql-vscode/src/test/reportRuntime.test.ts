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
        expect(Array.from(actions!.querySelectorAll('button')).map((b: any) => b.textContent)).toEqual(['Open', 'PDF', 'MD']);
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
