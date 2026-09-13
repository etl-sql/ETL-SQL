import assert from 'node:assert/strict';
import { isAllowedTokenName, isSafeCssValue, resolveDesignTokens, applyDesignTokens } from '../src/ETL-SQL.ReportRuntime/Resources/Shared/rt-theme.js';

for (const token of ['--etl-accent', '--etl-color-3', '--etl-series-revenue']) {
    assert.equal(isAllowedTokenName(token), true, token);
}
for (const token of ['--portal-private', '--etl-unknown', null, 42]) {
    assert.equal(isAllowedTokenName(token), false, String(token));
}
for (const value of ['url(https://example.com/x)', 'red; color: blue', 'var(--portal-secret)', 'expression(alert(1))', '<style>', 'red\nblue']) {
    assert.equal(isSafeCssValue(value), false, value);
}
for (const value of ['#38bdf8', 'rgba(1, 2, 3, 0.5)', 'var(--etl-accent)', '12px']) {
    assert.equal(isSafeCssValue(value), true, value);
}
const tokens = resolveDesignTokens({ BORDER_RADIUS: '12px', BORDER: '1px solid #334155', ACCENT: '#38bdf8', SHADOW: 'OFF' });
assert.equal(tokens['--etl-radius-sm'], '6px');
assert.equal(tokens['--etl-radius-md'], '12px');
assert.equal(tokens['--etl-radius-lg'], '18px');
assert.equal(tokens['--etl-border'], '#334155');
assert.equal(tokens['--etl-accent'], '#38bdf8');
assert.equal(tokens['--etl-shadow'], 'none');
assert.deepEqual(resolveDesignTokens(null), {});

// A second application removes obsolete series colors without clearing other scopes' properties.
const properties = new Map([['--etl-color-1', 'red'], ['--portal-private', 'keep']]);
const style = {
    0: '--etl-color-1', 1: '--portal-private', length: 2,
    removeProperty(name) { properties.delete(name); },
    setProperty(name, value) { properties.set(name, value); },
};
applyDesignTokens({ style }, { designTokens: { '--etl-accent': '#38bdf8', '--etl-color-2': 'green', '--etl-border': 'url(bad)', '--portal-private': 'overwrite' } });
assert.deepEqual(Object.fromEntries(properties), { '--portal-private': 'keep', '--etl-accent': '#38bdf8', '--etl-color-2': 'green' });
console.log('runtime theme: token allow-list, CSS safety, style projection and scoped application passed');
