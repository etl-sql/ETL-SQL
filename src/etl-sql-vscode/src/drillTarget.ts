import * as path from 'path';

const REPORT_EXTENSION = '.rptsql';

function isWithin(root: string, candidate: string): boolean {
    const relative = path.relative(path.resolve(root), path.resolve(candidate));
    return relative === '' || (!relative.startsWith('..') && !path.isAbsolute(relative));
}

/**
 * True when a drill target may be opened: a `.rptsql` report inside one of the allowed roots.
 * A preview builds (runs) whatever it opens, so a drill link must not reach a script outside the
 * workspace or a file of another kind.
 */
export function isAllowedDrillReport(candidate: string, allowedRoots: readonly string[]): boolean {
    return path.extname(candidate).toLowerCase() === REPORT_EXTENSION
        && allowedRoots.some(root => isWithin(root, candidate));
}

/**
 * Resolves a drill target relative to the current report. The `.rptsql` extension is added when
 * missing. Returns undefined when the result is not an allowed report.
 */
export function resolveDrillReport(currentScriptPath: string, target: string, allowedRoots: readonly string[]): string | undefined {
    if (!target) {
        return undefined;
    }
    let candidate = path.resolve(path.dirname(currentScriptPath), target);
    if (path.extname(candidate).toLowerCase() !== REPORT_EXTENSION) {
        candidate += REPORT_EXTENSION;
    }
    return isAllowedDrillReport(candidate, allowedRoots) ? candidate : undefined;
}
