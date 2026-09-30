/* eslint-disable @typescript-eslint/no-explicit-any */
import { describe, test, expect, beforeAll } from 'vitest';

/**
 * The inspector's interaction clauses: what a click runs, and a table's row detail.
 *
 * The rule under test is that the guided editor only takes over a clause it can write back
 * exactly. A clause it half understands comes back as custom or unsupported, and is left alone.
 */
describe('Studio interaction clauses', () => {
    let m: any;

    beforeAll(async () => {
        m = await import('../../../ETL-SQL.ReportRuntime/Resources/Shared/designer/designer-interactions.js');
    });

    test.each([
        'DRILL_DOWN(Target = Detail, Key = (Region, SaleYear))',
        'DRILL_IN(HIERARCHY = (Year, Quarter, Month))',
        'NAVIGATE_PAGE(Detail)',
        'SET_PARAMETER(@region, Region)',
    ])('a guided action writes back exactly what the formatter wrote: %s', text => {
        const action = m.readClickAction(text);
        expect(action.kind).not.toBe('CUSTOM');
        expect(m.writeClickAction(action)).toBe(text);
    });

    test('a single drill-down key without parentheses is still guided', () => {
        expect(m.readClickAction('drill_down(target = Detail, key = Region)'))
            .toEqual({ kind: 'DRILL_DOWN', target: 'Detail', keys: ['Region'] });
    });

    test.each([
        '(SET_PARAMETER(@region, Region), NAVIGATE_PAGE(Detail))',
        'SET_PARAMETER(@region, UPPER(Region))',
        'SET_PARAMETER(@from, @to, Region)',
        'CLEAR_FILTERS',
        "RUN_SCRIPT('refresh.sql')",
    ])('an action the guided editor cannot write stays custom: %s', text => {
        expect(m.readClickAction(text)).toEqual({ kind: 'CUSTOM', text });
    });

    test('an incomplete guided action writes nothing rather than a clause that will not parse', () => {
        expect(m.writeClickAction({ kind: 'DRILL_DOWN', target: 'Detail', keys: [] })).toBeNull();
        expect(m.writeClickAction({ kind: 'DRILL_DOWN', target: '', keys: ['Region'] })).toBeNull();
        expect(m.writeClickAction({ kind: 'NAVIGATE_PAGE', page: '' })).toBeNull();
        expect(m.writeClickAction({ kind: 'NONE' })).toBeNull();
    });

    test('a row detail reads its target, matches, and limit, and writes the formatter shape', () => {
        const text = 'ROW_DETAIL (TARGET = Detail, BINDINGS (@region = Region, @year = SaleYear), LIMIT = 50)';
        const detail = m.readRowDetail(text);
        expect(detail).toEqual({
            supported: true,
            target: 'Detail',
            bindings: [
                { childColumn: 'region', parentColumn: 'Region' },
                { childColumn: 'year', parentColumn: 'SaleYear' },
            ],
            limit: 50,
        });
        expect(m.writeRowDetail(detail)).toBe(text);
    });

    test('a row detail with only a target writes only its target', () => {
        expect(m.writeRowDetail(m.readRowDetail('ROW_DETAIL (TARGET = Detail)'))).toBe('ROW_DETAIL (TARGET = Detail)');
    });

    test.each([
        "ROW_DETAIL (TARGET = 'Detail Panel')",
        'ROW_DETAIL (TARGET = Detail, BINDINGS (@region = UPPER(Region)))',
        'ROW_DETAIL (TARGET = Detail, SOMETHING = ON)',
    ])('a row detail the editor cannot write back is left as authored: %s', text => {
        expect(m.readRowDetail(text)).toEqual({ supported: false, text });
    });

    test('a selection is keyed on MATCHING, else the category mapping', () => {
        expect(m.selectionKey({ 'interaction:MATCHING': 'Region' }, { X: 'Month' })).toBe('Region');
        expect(m.selectionKey({}, { X: 'Month', Y: 'Revenue' })).toBe('Month');
        expect(m.selectionKey({}, { Y: 'Revenue' })).toBeNull();
        expect(m.selectionKey({}, { X: 'SUM(Revenue)' })).toBeNull();
    });

    test('the parameters a query reads ignore quoted text', () => {
        expect([...m.parametersRead("SELECT * FROM #s WHERE Region = @Region AND Mail = 'a@b.com'")])
            .toEqual(['@region']);
    });

    // @Region is a LIST, so a Ctrl+click on several points matches every one of them.
    test.each([
        ['#sales', "(SELECT * FROM #sales WHERE 'All' IN @Region OR Region IN @Region)"],
        ['&sales', "(SELECT * FROM &sales WHERE 'All' IN @Region OR Region IN @Region)"],
        ['(SELECT SaleID, Region FROM #sales)', "(SELECT SaleID, Region FROM #sales WHERE 'All' IN @Region OR Region IN @Region)"],
    ])('a source it can edit safely is filtered on the parameter: %s', (source, expected) => {
        expect(m.filterSourceOn(source, 'Region')).toBe(expected);
    });

    test('the single-value filter Studio wrote earlier is upgraded to match several values', () => {
        expect(m.filterSourceOnSeveral("(SELECT * FROM #sales WHERE @Region = 'All' OR Region = @Region)", 'Region'))
            .toBe("(SELECT * FROM #sales WHERE 'All' IN @Region OR Region IN @Region)");
    });

    test.each([
        "(SELECT * FROM #sales WHERE Region = @Region)",
        "(SELECT * FROM #sales WHERE 'All' IN @Region OR Region IN @Region)",
        '#sales',
    ])('a hand-written or already-upgraded filter is left alone: %s', source => {
        expect(m.filterSourceOnSeveral(source, 'Region')).toBeNull();
    });

    test.each([
        '(SELECT * FROM #sales WHERE Total > 0)',
        '(SELECT Region, SUM(Total) AS T FROM #sales GROUP BY Region)',
        '(SELECT * FROM #a JOIN #b ON #a.id = #b.id)',
        '(SELECT * FROM (SELECT * FROM #sales) s)',
        '',
    ])('a source that already filters, groups, joins, or nests is left for the author: %s', source => {
        expect(m.filterSourceOn(source, 'Region')).toBeNull();
    });

    test.each([
        "TOOLTIP = 'Revenue for the month'",
        "TOOLTIP = 'It''s the month'",
        'TOOLTIP = Box',
        "TOOLTIP ('**Month detail**', VISUALS (Detail, Trend))",
        'TOOLTIP (VISUALS (Detail))',
        "TOOLTIP (FIELDS (Region, Revenue FORMAT 'C0', Share FORMAT 'P1'))",
        "TOOLTIP ('**Mix**', FIELDS (Region))",
    ])('a tooltip the inspector can edit writes back exactly what the designer reads: %s', clause => {
        const tooltip = m.readTooltip(clause);
        expect(tooltip.kind).not.toBe('CUSTOM');
        expect(m.writeTooltip(tooltip)).toBe(clause);
    });

    test('a tooltip text keeps its apostrophe', () => {
        expect(m.readTooltip("TOOLTIP = 'It''s the month'")).toEqual({ kind: 'TEXT', text: "It's the month" });
    });

    test.each([
        "TOOLTIP = 'Revenue: ' + CAST(@total AS VARCHAR)",
        "TOOLTIP ('x', VISUALS (Detail), FIELDS (Region))",
        'TOOLTIP (FIELDS (UPPER(Region)))',
    ])('a tooltip the inspector cannot write back is left as written: %s', clause => {
        expect(m.readTooltip(clause)).toEqual({ kind: 'CUSTOM', text: clause });
    });

    test('an unfinished tooltip writes nothing, and unfinished fields are left out', () => {
        expect(m.writeTooltip({ kind: 'TEXT', text: '  ' })).toBeNull();
        expect(m.writeTooltip({ kind: 'VISUALS', heading: 'x', visuals: [] })).toBeNull();
        expect(m.writeTooltip({ kind: 'FIELDS', heading: '', fields: [{ name: 'Region', format: '' }, { name: '', format: 'C0' }] }))
            .toBe('TOOLTIP (FIELDS (Region))');
    });

    test('a popover takes its hovered value from the first context mapping, as the build does', () => {
        expect(m.hoverContextColumn({ Y: 'Revenue', X: 'Month' })).toBe('Month');
        expect(m.hoverContextColumn({ LABEL: 'Name' })).toBe('Name');
        expect(m.hoverContextColumn({ VALUE: 'Total' })).toBeNull();
        expect(m.filterSourceOnHover('#sales', 'Month')).toBe('(SELECT * FROM #sales WHERE Month = @hover_value)');
    });

    test('an unfinished match is not written into the clause', () => {
        expect(m.writeRowDetail({
            supported: true,
            target: 'Detail',
            bindings: [{ childColumn: 'region', parentColumn: 'Region' }, { childColumn: '', parentColumn: '' }],
            limit: null,
        })).toBe('ROW_DETAIL (TARGET = Detail, BINDINGS (@region = Region))');
    });
});
