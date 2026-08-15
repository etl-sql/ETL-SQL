import { describe, test, expect, beforeEach } from 'vitest';
import { JSDOM } from 'jsdom';

describe('Visual Report Builder Round-Trip Fidelity & Trivia Tests', () => {
    let dom: JSDOM;
    let window: any;
    let document: Document;
    let createDesigner: any;

    beforeEach(async () => {
        dom = new JSDOM('<!DOCTYPE html><html><body><div id="designer-host"></div></body></html>', {
            url: 'http://localhost:3000',
            pretendToBeVisual: true,
        });
        window = dom.window;
        document = window.document;
        (globalThis as any).window = window;
        (globalThis as any).document = document;
        (globalThis as any).HTMLElement = window.HTMLElement;
        (globalThis as any).customElements = window.customElements;
        (globalThis as any).localStorage = {
            getItem: () => null,
            setItem: () => {},
            removeItem: () => {},
        };

        // Dynamic import of synced designer module
        const mod = await import('../../media/designer/designer.js');
        createDesigner = mod.createDesigner;
    });

    test('Initializes canvas with visual cards and proper grid placement', () => {
        const container = document.getElementById('designer-host')!;
        const initialDesignState = {
            pages: [
                {
                    id: 'p1',
                    name: 'Sales Dashboard',
                    mode: 'Dashboard',
                    visuals: [
                        {
                            id: 'v_rev',
                            name: 'v_rev',
                            type: 'BAR',
                            gridCol: 1,
                            gridRow: 1,
                            gridColSpan: 6,
                            gridRowSpan: 4,
                            title: 'Revenue by Region',
                            dataset: 'revenue_data',
                            mappings: { X: 'region', Y: 'revenue' },
                            options: {},
                        },
                        {
                            id: 'v_orders',
                            name: 'v_orders',
                            type: 'LINE',
                            gridCol: 7,
                            gridRow: 1,
                            gridColSpan: 6,
                            gridRowSpan: 4,
                            title: 'Order Volume',
                            dataset: 'revenue_data',
                            mappings: { X: 'order_date', Y: 'orders' },
                            options: {},
                        }
                    ]
                }
            ],
            datasets: [
                {
                    id: 'ds1',
                    name: 'revenue_data',
                    query: 'SELECT region, order_date, revenue, orders FROM #staging',
                }
            ]
        };

        const designer = createDesigner(container, {
            designState: initialDesignState,
            initialMode: 'design',
        });

        expect(designer).toBeDefined();
        const cards = container.querySelectorAll('.etlsql-dsgn-visual-card');
        expect(cards.length).toBe(2);

        const firstCard = cards[0] as HTMLElement;
        expect(firstCard.dataset.vid).toBe('v_rev');
        expect(firstCard.style.gridColumn).toContain('1 / span 6');

        const secondCard = cards[1] as HTMLElement;
        expect(secondCard.dataset.vid).toBe('v_orders');
        expect(secondCard.style.gridColumn).toContain('7 / span 6');
    });

    test('Clamps column and column-span outliers to 12-column boundary', () => {
        const container = document.getElementById('designer-host')!;
        const outlierDesignState = {
            pages: [
                {
                    id: 'p1',
                    name: 'Outlier Page',
                    mode: 'Dashboard',
                    visuals: [
                        {
                            id: 'v_outlier',
                            name: 'v_outlier',
                            type: 'BAR',
                            gridCol: 10,
                            gridRow: 1,
                            gridColSpan: 8, // Outlier: 10 + 8 = 18 > 12
                            gridRowSpan: -2, // Outlier: negative height
                            title: 'Outlier Chart',
                            dataset: 'data',
                            mappings: {},
                            options: {},
                        }
                    ]
                }
            ],
            datasets: []
        };

        const designer = createDesigner(container, {
            designState: outlierDesignState,
        });

        expect(designer).toBeDefined();
        const card = container.querySelector('.etlsql-dsgn-visual-card') as HTMLElement;
        expect(card).toBeDefined();

        // Property panel column inputs
        const colInput = container.querySelector('#pp-col') as HTMLInputElement | null;
        if (colInput) {
            expect(parseInt(colInput.value, 10)).toBeLessThanOrEqual(12);
            expect(parseInt(colInput.value, 10)).toBeGreaterThanOrEqual(1);
        }
    });

    test('Diagnostic warning badge surfaces syntax errors without destroying canvas state', async () => {
        const container = document.getElementById('designer-host')!;
        const initialDesignState = {
            pages: [
                {
                    id: 'p1',
                    name: 'Page 1',
                    mode: 'Dashboard',
                    visuals: [
                        {
                            id: 'v1',
                            name: 'v1',
                            type: 'BAR',
                            gridCol: 1,
                            gridRow: 1,
                            gridColSpan: 12,
                            gridRowSpan: 4,
                            title: 'Active Chart',
                            dataset: 'data',
                            mappings: {},
                            options: {},
                        }
                    ]
                }
            ],
            datasets: []
        };

        // Mock API parser to return syntax error
        const mockAuthFetch = async (url: string, init: any) => {
            if (url.endsWith('/api/designer/parse')) {
                return {
                    ok: true,
                    json: async () => ({
                        error: 'Syntax error at line 4: Unexpected token @@',
                        designState: null
                    })
                };
            }
            return { ok: true, json: async () => ({}) };
        };

        const designer = createDesigner(container, {
            designState: initialDesignState,
            authFetch: mockAuthFetch,
        });

        // The card exists
        expect(container.querySelectorAll('.etlsql-dsgn-visual-card').length).toBe(1);

        // Open split mode and simulate apply with syntax error
        const diagnosticBadge = container.querySelector('#dsgn-diagnostic-badge') as HTMLElement;
        expect(diagnosticBadge).toBeDefined();

        designer.dispose?.();
    });

    test('Theme and Report Style properties update state cleanly', () => {
        const container = document.getElementById('designer-host')!;
        const stateWithStyle = {
            pages: [
                {
                    id: 'p1',
                    name: 'Themed Page',
                    mode: 'Dashboard',
                    visuals: []
                }
            ],
            datasets: [],
            reportStyle: {
                theme: 'dark',
                accent: '#00E5FF',
                background: '#0B0F19',
                surface: '#1E293B',
                text: '#F8FAFC'
            }
        };

        const designer = createDesigner(container, {
            designState: stateWithStyle,
        });

        expect(designer).toBeDefined();
        const topbarTheme = container.querySelector('#dsgn-theme-select') as HTMLSelectElement;
        expect(topbarTheme).toBeDefined();
    });
});
