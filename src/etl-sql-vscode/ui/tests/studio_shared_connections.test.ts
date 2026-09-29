/* eslint-disable @typescript-eslint/no-explicit-any */
import { describe, test, expect, beforeAll } from 'vitest';

/**
 * A script names the shared connections it uses. A report that reads `sales_db.Sales` without
 * declaring it previews on the Portal and fails for every reader, so Studio writes the declaration.
 */
describe('Studio shared connection declarations', () => {
    let m: any;
    const shared = [{ alias: 'sales_db', connectorType: 'mssql' }, { alias: 'hr_db', connectorType: 'POSTGRES' }];

    beforeAll(async () => {
        m = await import('../../../ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-shared-connections.js');
    });

    test('a shared connection the script reads is declared once, at the top', () => {
        const script = 'CREATE DATASET &sales AS (SELECT Region FROM sales_db.Sales);\n';
        expect(m.declareSharedConnections(script, shared)).toBe(
            "CREATE CONNECTION sales_db AS MSSQL('SHARED:sales_db');\n\n" + script);
    });

    test('a connection the script already declares is left alone, however it was declared', () => {
        const script = "CREATE CONNECTION [sales_db] AS MSSQL(CONNECTION_STRING = 'x');\nSELECT * INTO #s FROM sales_db.Sales;";
        expect(m.declareSharedConnections(script, shared)).toBe(script);
    });

    test('a write counts as a use', () => {
        const script = 'INSERT INTO hr_db.People SELECT * FROM #people;';
        expect(m.declareSharedConnections(script, shared)).toContain("CREATE CONNECTION hr_db AS POSTGRES('SHARED:hr_db');");
    });

    test('a name in a comment, a string, or a column reference is not a read', () => {
        const script = [
            '-- FROM sales_db.Sales was the old source',
            "SELECT 'FROM sales_db.Sales' AS note INTO #n;",
            'SELECT sales_db.Region FROM #s AS sales_db;',
        ].join('\n');
        expect(m.declareSharedConnections(script, shared)).toBe(script);
    });

    test('aliases the host does not share are never declared', () => {
        const script = 'SELECT * INTO #x FROM local_db.T;';
        expect(m.declareSharedConnections(script, shared)).toBe(script);
    });

    test('Windows line endings are kept', () => {
        const script = 'SELECT * INTO #s FROM sales_db.Sales;\r\nSELECT 1;';
        expect(m.declareSharedConnections(script, shared))
            .toBe("CREATE CONNECTION sales_db AS MSSQL('SHARED:sales_db');\r\n\r\n" + script);
    });
});
