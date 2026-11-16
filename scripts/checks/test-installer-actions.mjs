import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

// No COM or installer APIs escape this isolated context. Exercise the authored JScript functions.
const source = readFileSync(new URL('../../src/ETL-SQL.Installer/installer-actions.js', import.meta.url), 'utf8');
// JScript supports COM indexed-property assignment; V8 needs an equivalent fake setter.
const executable = source.replace(/Session\.Property\("(\w+)"\)\s*=\s*([^;\r\n]+);/g,
  (_, key, value) => `Session.SetProperty(${JSON.stringify(key)}, ${value});`);
function action(name, { data = 'C:\\fixture\\|UPGRADE=fixture', run = 0, failure = false } = {}) {
  const values = { CustomActionData: data, INSTALLFOLDER: 'C:\\fixture\\' };
  const context = vm.createContext({
    Session: { Property(key) { return values[key] ?? ''; }, SetProperty(key, value) { values[key] = value; } },
    ActiveXObject: function (kind) {
      if (failure === true) throw new Error('Synthetic COM failure');
      if (kind === 'WScript.Shell') return { Run() { return run; } };
      if (kind === 'Scripting.FileSystemObject') return { FileExists() { return true; } };
      if (kind === 'ADODB.Stream') return {
        Open() {}, Close() {}, LoadFromFile() {},
        ReadText() { if (failure === 'read') throw new Error('Synthetic read failure'); return '{"value":"中文"}'; },
        WriteText() {}, SaveToFile() { if (failure === 'save') throw new Error('Synthetic write failure'); }
      };
      throw new Error('Unexpected fake COM API');
    }
  });
  vm.runInContext(executable, context);
  return { status: vm.runInContext(`${name}()`, context), values };
}
assert.equal(action('ConfigureJwt').status, 1);
assert.equal(action('ConfigureJwt', { run: 17 }).status, 3);
assert.equal(action('ConfigureJwt', { failure: true }).status, 3);
assert.equal(action('ConfigureJwt', { data: '' }).status, 3);
assert.equal(action('ReadSettings').status, 1);
assert.equal(action('ReadSettings').values.PREVIOUS_SETTINGS, '{"value":"中文"}');
assert.equal(action('ReadSettings', { failure: true }).status, 3);
assert.equal(action('ReadSettings', { failure: 'read' }).status, 3);
assert.equal(action('RestoreSettings', { data: 'C:\\fixture\\|{"value":"中文"}' }).status, 1);
assert.equal(action('RestoreSettings', { failure: true }).status, 3);
assert.equal(action('RestoreSettings', { failure: 'save' }).status, 3);
assert.equal(action('RestoreSettings', { data: '' }).status, 3);
const manifest = readFileSync(new URL('../../src/ETL-SQL.Installer/Installer.wxs', import.meta.url), 'utf8');
for (const name of ['WriteJwt', 'ReadSettings', 'RestoreSettings']) {
  const element = manifest.match(new RegExp(`<CustomAction\\s+Id="${name}"[^>]*>`))?.[0];
  assert.ok(element?.includes('Return="check"'), `${name} must propagate failure to MSI`);
  if (name !== 'ReadSettings') assert.ok(element.includes('Impersonate="no"'), `${name} must run in the installer service context`);
}
console.log('Installer actions: successful configuration/preservation and COM/process failures propagate through checked MSI actions.');
