const fs = require('fs');
const path = require('path');

const repoRoot = path.resolve(__dirname, '..');

// Helper to find all markdown files recursively (excluding Docs_Legacy, Help_Legacy and node_modules)
function getMarkdownFiles(dir) {
  let results = [];
  const list = fs.readdirSync(dir);
  list.forEach(file => {
    const fullPath = path.join(dir, file);
    const stat = fs.statSync(fullPath);
    if (stat && stat.isDirectory()) {
      if (file !== 'Docs_Legacy' && file !== 'node_modules' && file !== '.git' && file !== 'bin' && file !== 'obj' && file !== 'Help_Legacy') {
        results = results.concat(getMarkdownFiles(fullPath));
      }
    } else if (file.endsWith('.md')) {
      results.push(fullPath);
    }
  });
  return results;
}

const allFiles = getMarkdownFiles(repoRoot);

// Map of filename (lowercase) to absolute path
const fileMap = {};
allFiles.forEach(f => {
  fileMap[path.basename(f).toLowerCase()] = f;
});

// Add standard root files to fileMap explicitly
const rootFiles = ['README.md', 'CHANGELOG.md', 'AGENTS.md', 'CLAUDE.md', 'CONTRIBUTING.md', 'DEVELOPER.md', 'GEMINI.md', 'LICENSE.md', 'SECURITY.md', 'TRADEMARK.md'];
rootFiles.forEach(rf => {
  fileMap[rf.toLowerCase()] = path.join(repoRoot, rf);
});

let fixedCount = 0;

allFiles.forEach(filePath => {
  const content = fs.readFileSync(filePath, 'utf8');
  const fileDir = path.dirname(filePath);
  
  const updatedContent = content.replace(/\[([^\]]+)\]\(([^)]+)\)/g, (match, label, link) => {
    if (link.startsWith('http') || link.startsWith('#') || link.startsWith('mailto:')) {
      return match;
    }

    const hashIndex = link.indexOf('#');
    let cleanLink = hashIndex !== -1 ? link.substring(0, hashIndex) : link;
    const anchor = hashIndex !== -1 ? link.substring(hashIndex) : '';

    // Handle file:/// URL scheme
    let isFileUri = false;
    if (cleanLink.startsWith('file:///')) {
      isFileUri = true;
      cleanLink = cleanLink.substring(8);
    }

    // Clean up link separators
    let lookupLink = cleanLink.replace(/\\/g, '/');

    // CRITICAL: If the relative path already resolves correctly on disk, do NOT change it!
    let localResolved = path.resolve(fileDir, lookupLink);
    if (fs.existsSync(localResolved)) {
      return match;
    }

    // 1. Resolve samples links
    if (lookupLink.includes('samples/')) {
      const idx = lookupLink.indexOf('samples/');
      const samplePart = lookupLink.substring(idx);
      const targetAbs = path.join(repoRoot, samplePart);
      let newRel = path.relative(fileDir, targetAbs).replace(/\\/g, '/');
      if (isFileUri) {
        return `[${label}](file:///${targetAbs.replace(/\\/g, '/')}${anchor})`;
      }
      return `[${label}](${newRel}${anchor})`;
    }

    // 2. Resolve assets links
    if (lookupLink.includes('assets/')) {
      const idx = lookupLink.indexOf('assets/');
      const assetPart = lookupLink.substring(idx);
      const targetAbs = path.join(repoRoot, 'docs', assetPart);
      let newRel = path.relative(fileDir, targetAbs).replace(/\\/g, '/');
      if (isFileUri) {
        return `[${label}](file:///${targetAbs.replace(/\\/g, '/')}${anchor})`;
      }
      return `[${label}](${newRel}${anchor})`;
    }

    // 3. Resolve using fileMap for markdown files
    const filename = path.basename(lookupLink).toLowerCase();
    const targetAbs = fileMap[filename] || fileMap[filename.replace(/\./g, '-')];
    if (targetAbs) {
      let newRel = path.relative(fileDir, targetAbs).replace(/\\/g, '/');
      if (isFileUri) {
        return `[${label}](file:///${targetAbs.replace(/\\/g, '/')}${anchor})`;
      }
      return `[${label}](${newRel}${anchor})`;
    }

    // Try resolving if file moved 1 level deeper (e.g. Docs/ -> docs/guides/)
    let resolvedDeeper = path.resolve(fileDir, '..', lookupLink);
    if (fs.existsSync(resolvedDeeper)) {
      let newRel = path.relative(fileDir, resolvedDeeper).replace(/\\/g, '/');
      return `[${label}](${newRel}${anchor})`;
    }

    // Try resolving if file moved 2 levels deeper (e.g. Docs/ -> docs/architecture/decisions/)
    let resolvedDeeper2 = path.resolve(fileDir, '../..', lookupLink);
    if (fs.existsSync(resolvedDeeper2)) {
      let newRel = path.relative(fileDir, resolvedDeeper2).replace(/\\/g, '/');
      return `[${label}](${newRel}${anchor})`;
    }

    return match;
  });

  if (content !== updatedContent) {
    fs.writeFileSync(filePath, updatedContent, 'utf8');
    fixedCount++;
  }
});

console.log(`Relative link repair complete. Fixed links in ${fixedCount} files.`);
