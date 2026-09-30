/* eslint-disable @typescript-eslint/no-explicit-any */
import { describe, test, expect, beforeAll } from 'vitest';

/**
 * The analytics overlays editor. The clauses below are the formatter's own shapes, the same ones
 * OverlayDesignerRoundTripTests pins on the server, so a clause the designer hands over reads into
 * rows and writes back byte for byte.
 */
describe('Studio analytics overlays', () => {
    let m: any;

    beforeAll(async () => {
        m = await import('../../../ETL-SQL.ReportRuntime/Resources/Shared/designer/designer-overlays.js');
    });

    test.each([
        'OVERLAYS (GOAL(100) AS DASHED)',
        "OVERLAYS (GOAL(100) AS SOLID WITH (COLOR = '#dc2626', LABEL = 'Target'))",
        "OVERLAYS (AVERAGE AS DOTTED WITH (LABEL = 'Mean'))",
        'OVERLAYS (MOVING_AVG(3) AS SOLID)',
        'OVERLAYS (LINEAR AS DASHED, POLYNOMIAL(2) AS DOTTED)',
        "OVERLAYS (REFERENCE_LINE (VALUE = 50, LABEL = 'Floor', STYLE = SOLID, COLOR = '#16a34a'))",
        'OVERLAYS (REFERENCE_LINE (VALUE = -2.5, STYLE = DASHED))',
        "OVERLAYS (REFERENCE_BAND (LOW = 10, HIGH = 20, COLOR = '#fde68a', LABEL = 'Normal'))",
        'OVERLAYS (FORECAST(Forecast) AS DASHED WITH (CONFIDENCE_LOW = Low, CONFIDENCE_HIGH = High))',
        'OVERLAYS (RUNNING_TOTAL(RunningRevenue) AS SOLID)',
        "OVERLAYS (GOAL(100) AS DASHED, ANNOTATIONS (POINT (TYPE = MAX, LABEL = 'Peak')))",
        "OVERLAYS (ANNOTATIONS (POINT (SERIES = 'Revenue', TYPE = MIN, LABEL = 'Low', SYMBOL = 'arrow', COLOR = '#dc2626')))",
        "OVERLAYS (ANNOTATIONS (POINT (TYPE = COORD('Mar', 120), LABEL = 'Launch', SYMBOL = 'circle')))",
        "OVERLAYS (ANNOTATIONS (POINT (SERIES = 'trend', TYPE = COORD(-1.5, -20))))",
    ])('the formatter shape reads into rows and writes back exactly: %s', clause => {
        expect(m.writeOverlays(m.readOverlays(clause))).toBe(clause);
    });

    test('an entry the editor cannot write is kept as written, beside the ones it can', () => {
        // Two points in one ANNOTATIONS group are not one card's worth.
        const group = "ANNOTATIONS (POINT (TYPE = MAX), POINT (TYPE = MIN))";
        const rows = m.readOverlays(`OVERLAYS (${group}, AVERAGE AS DASHED)`);
        expect(rows.map((row: any) => row.kind)).toEqual(['RAW', 'AVERAGE']);
        rows[1].style = 'SOLID';
        expect(m.writeOverlays(rows)).toBe(`OVERLAYS (${group}, AVERAGE AS SOLID)`);
    });

    test('an annotation point reads into a card, and a category x stays text', () => {
        const [point] = m.readOverlays("OVERLAYS (ANNOTATIONS (POINT (TYPE = COORD('3', 120), LABEL = 'Q3')))");
        expect(point).toMatchObject({ kind: 'ANNOTATION', target: 'COORD', x: '3', xIsText: true, y: '120', symbol: 'pin' });
        // Written back as the category '3', not the number 3.
        expect(m.writeEntry(point)).toBe("ANNOTATIONS (POINT (TYPE = COORD('3', 120), LABEL = 'Q3'))");
        expect(m.writeEntry({ ...point, xIsText: false })).toBe("ANNOTATIONS (POINT (TYPE = COORD(3, 120), LABEL = 'Q3'))");
    });

    test('a point without a TYPE is kept as written, since the card would add one', () => {
        expect(m.readOverlays("OVERLAYS (ANNOTATIONS (POINT (LABEL = 'Peak')))")[0].kind).toBe('RAW');
    });

    test('a label with an apostrophe or a comma stays one value', () => {
        const rows = m.readOverlays("OVERLAYS (GOAL(5) AS DASHED WITH (LABEL = 'Q1, Q2 ''plan'''))");
        expect(rows[0]).toMatchObject({ kind: 'GOAL', value: '5', label: "Q1, Q2 'plan'" });
    });

    test('rows the build would refuse are left out until they are complete', () => {
        const blankGoal = m.blankOverlay('GOAL');
        expect(m.writeEntry(blankGoal)).toBeNull();
        expect(m.writeEntry({ ...m.blankOverlay('REFERENCE_BAND'), low: '20', high: '10' })).toBeNull();
        // The build refuses one confidence bound without the other.
        expect(m.writeEntry({ ...m.blankOverlay('FORECAST'), field: 'Forecast', low: 'Low' })).toBeNull();
        // A chosen point needs both coordinates.
        expect(m.writeEntry({ ...m.blankOverlay('ANNOTATION'), target: 'COORD', x: 'Mar' })).toBeNull();
        expect(m.writeEntry(m.blankOverlay('ANNOTATION'))).toBe('ANNOTATIONS (POINT (TYPE = MAX))');
        expect(m.writeOverlays([blankGoal])).toBeNull();
        expect(m.writeOverlays([blankGoal, m.blankOverlay('AVERAGE')])).toBe('OVERLAYS (AVERAGE AS DASHED)');
    });

    test('each chart is offered only the overlays its build accepts', () => {
        const kinds = (type: string) => m.overlaysFor(type).map((item: any) => item.kind);
        expect(kinds('LINE')).toContain('FORECAST');
        expect(kinds('BAR')).not.toContain('FORECAST');
        expect(kinds('SCATTER')).not.toContain('RUNNING_TOTAL');
        expect(kinds('PIE')).toEqual([]);
    });
});
