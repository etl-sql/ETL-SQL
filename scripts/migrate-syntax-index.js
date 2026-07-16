const fs = require('fs');
const path = require('path');

const repoRoot = path.resolve(__dirname, '..');
const legacyIndexPath = path.join(repoRoot, 'Docs_Legacy', 'Syntax_Index.md');
const newIndexPath = path.join(repoRoot, 'docs', 'Syntax_Index.md');

// Load all file paths in the new docs/ folder so we can search for them
function walkDir(dir, fileList = []) {
  const files = fs.readdirSync(dir);
  for (const file of files) {
    const filePath = path.join(dir, file);
    const stat = fs.statSync(filePath);
    if (stat.isDirectory()) {
      walkDir(filePath, fileList);
    } else {
      fileList.push(filePath);
    }
  }
  return fileList;
}

const allNewFiles = walkDir(path.join(repoRoot, 'docs')).map(p => 
  path.relative(path.join(repoRoot, 'docs'), p).replace(/\\/g, '/')
);

// Map old guide links
const guideMap = {
  '../Docs/Reference/Grammar.md': 'guides/getting-started.md',
  '../Docs/Reference/Data_Connectors.md': 'guides/administration.md',
  '../Docs/Reference/Standard_Library.md': 'guides/getting-started.md',
  '../Docs/Reference/Specialized_Operations.md': 'guides/administration.md',
  '../Docs/Reference/RelativeDate_Parameters.md': 'reference/dates-times/reldate.md',
  '../Docs/RelativeDate_Parameters.md': 'reference/dates-times/reldate.md',
  '../Docs/Report_SQL_Guide.md': 'guides/report-sql.md',
  '../Docs/Administrators_Guide.md': 'guides/administration.md',
  '../Docs/Cookbook.md': 'cookbooks/etl-recipes.md',
  '../Docs/Report_Cookbook.md': 'cookbooks/report-recipes.md',
  '../Docs/Reference/Lineage.md': 'reference/statements/lineage.md'
};

function resolveNewLink(oldLink) {
  // If it's a web link, return as is
  if (oldLink.startsWith('http')) {
    return oldLink;
  }

  // Remove anchor hash for lookup, but preserve it for output
  const hashIndex = oldLink.indexOf('#');
  let cleanLink = hashIndex !== -1 ? oldLink.substring(0, hashIndex) : oldLink;
  const anchor = hashIndex !== -1 ? oldLink.substring(hashIndex) : '';

  // Check guide map first
  const mappedGuide = guideMap[cleanLink];
  if (mappedGuide) {
    return mappedGuide + anchor;
  }

  // Extract the filename (e.g. SELECT.md or ABS.md)
  const filename = path.basename(cleanLink).toLowerCase();

  // Search in our new files list
  // The filename might match exactly or match case-insensitively
  const match = allNewFiles.find(f => 
    path.basename(f).toLowerCase() === filename ||
    path.basename(f).toLowerCase().replace(/_/g, '') === filename.replace(/_/g, '') ||
    path.basename(f).toLowerCase().replace(/\./g, '-') === filename.replace(/\./g, '-')
  );

  if (match) {
    return match + anchor;
  }

  // Fallbacks for known file translations
  if (filename === 'try.md') {
    const tryMatch = allNewFiles.find(f => path.basename(f).toLowerCase() === 'try-catch.md');
    if (tryMatch) return tryMatch + anchor;
  }
  if (filename === 'bulk.insert.md') {
    const bulkMatch = allNewFiles.find(f => path.basename(f).toLowerCase() === 'bulk-insert.md');
    if (bulkMatch) return bulkMatch + anchor;
  }
  if (filename === 'copy.md') {
    const copyMatch = allNewFiles.find(f => path.basename(f).toLowerCase() === 'copy-file.md');
    if (copyMatch) return copyMatch + anchor;
  }
  if (filename === 'compress.md') {
    const compressMatch = allNewFiles.find(f => path.basename(f).toLowerCase() === 'compress-file.md');
    if (compressMatch) return compressMatch + anchor;
  }
  if (filename === 'encrypt.md') {
    const encryptMatch = allNewFiles.find(f => path.basename(f).toLowerCase() === 'encrypt-file.md');
    if (encryptMatch) return encryptMatch + anchor;
  }
  if (filename === 'create.pgp_key_pair.md') {
    const keyMatch = allNewFiles.find(f => path.basename(f).toLowerCase() === 'create-pgp-key-pair.md');
    if (keyMatch) return keyMatch + anchor;
  }
  if (filename === 'create.ssh_key_pair.md') {
    const keyMatch = allNewFiles.find(f => path.basename(f).toLowerCase() === 'create-ssh-key-pair.md');
    if (keyMatch) return keyMatch + anchor;
  }
  if (filename === 'email.md') {
    const emailMatch = allNewFiles.find(f => path.basename(f).toLowerCase() === 'send-email.md');
    if (emailMatch) return emailMatch + anchor;
  }

  // If no match found, keep the old link so we can audit it in Gate 1
  return oldLink;
}

const content = fs.readFileSync(legacyIndexPath, 'utf8');

// Replace all markdown links [label](path)
const updatedContent = content.replace(/\[([^\]]+)\]\(([^)]+)\)/g, (match, label, link) => {
  const newLink = resolveNewLink(link);
  return `[${label}](${newLink})`;
});

fs.writeFileSync(newIndexPath, updatedContent, 'utf8');
console.log('Syntax_Index.md migrated to docs/Syntax_Index.md with resolved links.');
