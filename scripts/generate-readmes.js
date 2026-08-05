#!/usr/bin/env node
const fs = require('fs');
const path = require('path');

const repoRoot = path.resolve(__dirname, '..');
const docsDir = path.join(repoRoot, 'docs');

function getMarkdownFiles(dir) {
  return fs.readdirSync(dir)
    .filter(file => file.endsWith('.md') && file.toLowerCase() !== 'readme.md')
    .map(file => path.join(dir, file));
}

function getDirectories(srcpath) {
  return fs.readdirSync(srcpath)
    .map(file => path.join(srcpath, file))
    .filter(filePath => fs.statSync(filePath).isDirectory());
}

function scanDirectories(dir, callback) {
  callback(dir);
  getDirectories(dir).forEach(subdir => {
    const name = path.basename(subdir).toLowerCase();
    if (name !== 'assets' && name !== 'bin' && name !== 'obj' && name !== '.git') {
      scanDirectories(subdir, callback);
    }
  });
}

function cleanMarkdownLinks(text) {
  // Replace [text](url) with text
  return text.replace(/\[([^\]]+)\]\([^)]+\)/g, '$1');
}

function extractMetadata(filePath) {
  const content = fs.readFileSync(filePath, 'utf8');
  const lines = content.split(/\r?\n/);
  
  let title = path.basename(filePath, '.md');
  let description = '';
  
  // Try to find H1 header
  for (let line of lines) {
    line = line.trim();
    if (line.startsWith('# ')) {
      title = line.substring(2).trim();
      break;
    }
  }
  
  // Try to find first description paragraph
  let foundTitle = false;
  let inFence = false;
  for (let line of lines) {
    line = line.trim();
    // Track fenced blocks. Without this the first *line inside* a code block becomes the page
    // description, which is how one guide came to describe itself as
    // "ETL-SQL run nightly_load.etlsql --log".
    if (line.startsWith('```')) {
      inFence = !inFence;
      continue;
    }
    if (inFence) continue;
    if (line.startsWith('# ')) {
      foundTitle = true;
      continue;
    }
    // Skip anything that is not prose about the page.
    //
    // `<!--` matters: several pages carry an AST-name marker (`<!-- SearchPortalCatalogStatement -->`)
    // directly under the title, and those were being published as the page's description in the
    // generated index. `#` matters because a page whose title is followed straight by a section
    // heading was described by that heading — "## 8. Backup & Maintenance" — which told a reader
    // nothing and went stale the moment the heading was renumbered. `>` keeps callouts such as the
    // "Applies to" profile banner out of the description slot.
    if (line === ''
      || line.startsWith('---') || line.startsWith('|') || line.startsWith('`')
      || line.startsWith('/*') || line.startsWith('//')
      || line.startsWith('<!--') || line.startsWith('#') || line.startsWith('>')) {
      continue;
    }
    // Clean and truncate
    description = cleanMarkdownLinks(line);
    if (description.length > 150) {
      description = description.substring(0, 147) + '...';
    }
    break;
  }
  
  return { title, description };
}

scanDirectories(docsDir, dir => {
  const mdFiles = getMarkdownFiles(dir);
  if (mdFiles.length > 5) {
    const relativeDir = path.relative(docsDir, dir);
    if (relativeDir.replace(/\\/g, '/').toLowerCase() === 'reference/cli') {
      return;
    }
    console.log(`Generating README.md for docs/${relativeDir || ''}`);
    
    // Generate markdown content
    let content = `# ${path.basename(dir).toUpperCase()} Reference\n\n`;
    
    // Parent link
    const parentDir = path.dirname(dir);
    if (parentDir.startsWith(docsDir)) {
      content += `[« Back to parent](../README.md)\n\n`;
    } else {
      content += `[« Back to home](../README.md)\n\n`;
    }
    
    content += `| Page | Description |\n`;
    content += `| :--- | :--- |\n`;
    
    mdFiles.sort().forEach(file => {
      const { title, description } = extractMetadata(file);
      const relativePath = path.basename(file);
      content += `| [${title}](${relativePath}) | ${description} |\n`;
    });
    
    fs.writeFileSync(path.join(dir, 'README.md'), content, 'utf8');
  }
});

console.log('Readme generation complete.');
