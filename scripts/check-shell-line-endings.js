// Check and enforce LF line endings for all shell (.sh) scripts in the repository.
// Usage:
//   node scripts/check-shell-line-endings.js        (check mode - exits 1 on CRLF)
//   node scripts/check-shell-line-endings.js --fix  (converts CRLF to LF)

const fs = require('fs');
const path = require('path');

const isFix = process.argv.includes('--fix');
const repoRoot = path.resolve(__dirname, '..');

function findShFiles(dir, fileList = []) {
  const entries = fs.readdirSync(dir, { withFileTypes: true });
  for (const entry of entries) {
    if (entry.isDirectory()) {
      if (entry.name === 'node_modules' || entry.name === '.git' || entry.name === '.vscode-test' || entry.name === 'bin' || entry.name === 'obj') continue;
      findShFiles(path.join(dir, entry.name), fileList);
    } else if (entry.isFile() && entry.name.endsWith('.sh')) {
      fileList.push(path.join(dir, entry.name));
    }
  }
  return fileList;
}

const shFiles = findShFiles(repoRoot);
const crlfFiles = [];

for (const filePath of shFiles) {
  const content = fs.readFileSync(filePath, 'utf8');
  if (content.includes('\r')) {
    crlfFiles.push(filePath);
    if (isFix) {
      const normalized = content.replace(/\r\n/g, '\n').replace(/\r/g, '\n');
      fs.writeFileSync(filePath, normalized, 'utf8');
      console.log(`Normalized LF: ${path.relative(repoRoot, filePath)}`);
    }
  }
}

if (!isFix && crlfFiles.length > 0) {
  console.error(`ERROR: The following shell (.sh) script(s) contain CRLF line endings:`);
  for (const f of crlfFiles) {
    console.error(`  - ${path.relative(repoRoot, f)}`);
  }
  console.error(`Run 'node scripts/check-shell-line-endings.js --fix' to normalize line endings.`);
  process.exit(1);
}

if (isFix) {
  console.log(`Successfully normalized ${crlfFiles.length} shell script(s) to LF.`);
} else {
  console.log(`All ${shFiles.length} shell (.sh) scripts have valid LF line endings.`);
}
