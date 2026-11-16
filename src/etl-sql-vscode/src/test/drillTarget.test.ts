import { describe, it, expect } from 'vitest';
import * as path from 'path';
import { resolveDrillReport, isAllowedDrillReport } from '../drillTarget';

describe('resolveDrillReport', () => {
    const root = path.resolve('/work/project');
    const current = path.join(root, 'reports', 'sales.rptsql');

    it('resolves a sibling report and adds the extension', () => {
        expect(resolveDrillReport(current, 'detail', [root])).toBe(path.join(root, 'reports', 'detail.rptsql'));
        expect(resolveDrillReport(current, '../other/detail.rptsql', [root])).toBe(path.join(root, 'other', 'detail.rptsql'));
    });

    it('refuses a target that climbs out of the workspace', () => {
        expect(resolveDrillReport(current, '../../../outside/evil.rptsql', [root])).toBeUndefined();
        expect(resolveDrillReport(current, path.resolve('/etc/evil.rptsql'), [root])).toBeUndefined();
    });

    it('never opens a file that is not a report', () => {
        // "pipeline.etlsql" becomes "pipeline.etlsql.rptsql", which is a report name, not the script.
        expect(resolveDrillReport(current, 'pipeline.etlsql', [root])).toBe(path.join(root, 'reports', 'pipeline.etlsql.rptsql'));
        expect(isAllowedDrillReport(path.join(root, 'reports', 'pipeline.etlsql'), [root])).toBe(false);
    });

    it('rejects a sibling folder that only shares a name prefix with the root', () => {
        expect(isAllowedDrillReport(path.resolve('/work/project-evil/x.rptsql'), [root])).toBe(false);
    });
});
