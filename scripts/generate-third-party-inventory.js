const fs = require('fs');
const os = require('os');
const path = require('path');

const repoRoot = path.resolve(__dirname, '..');
const defaultOutput = path.join(repoRoot, 'THIRD-PARTY-INVENTORY.md');

const args = process.argv.slice(2);
const checkOnly = args.includes('--check');
const outputArgIndex = args.indexOf('--output');
const outputPath = outputArgIndex >= 0 && args[outputArgIndex + 1]
  ? path.resolve(repoRoot, args[outputArgIndex + 1])
  : defaultOutput;

const bundledAssets = [
  {
    component: 'Apache ECharts',
    files: 'src/ETL-SQL.ReportRuntime/Resources/Shared/echarts.min.js',
    license: 'Apache-2.0',
    project: 'https://echarts.apache.org/'
  },
  {
    component: 'Chart.js',
    files: 'src/ETL-SQL.ReportRuntime/Resources/Shared/chart.min.js',
    license: 'MIT',
    project: 'https://www.chartjs.org/'
  },
  {
    component: 'Tabulator',
    files: 'src/ETL-SQL.ReportRuntime/Resources/Shared/tabulator.min.js; src/ETL-SQL.ReportRuntime/Resources/Shared/tabulator.min.css',
    license: 'MIT',
    project: 'https://tabulator.info/'
  },
  ...readCodeMirrorLockAssets()
];

const npmLicenseFallbacks = {
  '@eslint/js': 'MIT',
  '@tailwindcss/vite': 'MIT',
  '@tanstack/react-table': 'MIT',
  '@types/glob': 'MIT',
  '@types/jsdom': 'MIT',
  '@types/mocha': 'MIT',
  '@types/node': 'MIT',
  '@types/react': 'MIT',
  '@types/react-dom': 'MIT',
  '@types/vscode': 'MIT',
  '@typescript-eslint/eslint-plugin': 'MIT',
  '@typescript-eslint/parser': 'MIT',
  '@vitejs/plugin-react': 'MIT',
  '@vitest/coverage-v8': 'MIT',
  '@vscode/test-electron': 'MIT',
  '@vscode/webview-ui-toolkit': 'MIT',
  'clsx': 'MIT',
  'echarts': 'Apache-2.0',
  'eslint': 'MIT',
  'eslint-plugin-react-hooks': 'MIT',
  'eslint-plugin-react-refresh': 'MIT',
  'framer-motion': 'MIT',
  'glob': 'ISC',
  'globals': 'MIT',
  'jsdom': 'MIT',
  'lucide-react': 'ISC',
  'mocha': 'MIT',
  'react': 'MIT',
  'react-dom': 'MIT',
  'tailwind-merge': 'MIT',
  'tailwindcss': 'MIT',
  'typescript': 'Apache-2.0',
  'typescript-eslint': 'MIT',
  'vite': 'MIT',
  'vite-plugin-singlefile': 'MIT',
  'vitest': 'MIT',
  'vscode-languageclient': 'MIT'
};

function readCodeMirrorLockAssets() {
  const bundleFile = 'src/ETL-SQL.ReportRuntime/Resources/Shared/designer/codemirror/codemirror-bundle.min.js';
  const manifestFiles = 'scripts/codemirror/package.json; scripts/codemirror/package-lock.json';
  const metadata = {
    '@codemirror/state': { component: 'CodeMirror @codemirror/state', files: bundleFile, project: 'https://codemirror.net/' },
    '@codemirror/view': { component: 'CodeMirror @codemirror/view', files: bundleFile, project: 'https://codemirror.net/' },
    '@codemirror/commands': { component: 'CodeMirror @codemirror/commands', files: bundleFile, project: 'https://codemirror.net/' },
    '@codemirror/language': { component: 'CodeMirror @codemirror/language', files: bundleFile, project: 'https://codemirror.net/' },
    '@codemirror/search': { component: 'CodeMirror @codemirror/search', files: bundleFile, project: 'https://codemirror.net/' },
    '@codemirror/autocomplete': { component: 'CodeMirror @codemirror/autocomplete', files: bundleFile, project: 'https://codemirror.net/' },
    '@codemirror/lint': { component: 'CodeMirror @codemirror/lint', files: bundleFile, project: 'https://codemirror.net/' },
    '@lezer/highlight': { component: 'Lezer @lezer/highlight', files: bundleFile, project: 'https://lezer.codemirror.net/' },
    'esbuild': { component: 'esbuild', files: manifestFiles, project: 'https://esbuild.github.io/' }
  };
  const lockPath = path.join(repoRoot, 'scripts', 'codemirror', 'package-lock.json');
  const lock = JSON.parse(fs.readFileSync(lockPath, 'utf8'));
  return Object.keys(lock.packages[''].dependencies || {})
    .filter(name => metadata[name])
    .map(name => {
      const pkg = lock.packages[`node_modules/${name}`];
      const meta = metadata[name];
      if (!pkg?.version) throw new Error(`Missing ${name} in CodeMirror package-lock.json`);
      return {
        component: `${meta.component} ${pkg.version}`,
        files: meta.files,
        license: pkg.license || npmLicenseFallbacks[name] || '',
        project: meta.project
      };
    });
}

function readText(filePath) {
  return fs.readFileSync(filePath, 'utf8');
}

function writeText(filePath, text) {
  fs.writeFileSync(filePath, text, 'utf8');
}

function walk(dir, predicate, output = []) {
  if (!fs.existsSync(dir)) return output;
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      if (entry.name === 'bin'
        || entry.name === 'obj'
        || entry.name === 'node_modules'
        || entry.name === '.git'
        || entry.name === '.worktrees'
        // Claude Code checks out git worktrees under .claude/worktrees; scanning them
        // duplicates every project reference under a second, non-canonical path.
        || entry.name === '.claude') continue;
      walk(full, predicate, output);
    } else if (predicate(full)) {
      output.push(full);
    }
  }
  return output;
}

function xmlAttr(tag, name) {
  const match = tag.match(new RegExp(`${name}\\s*=\\s*"([^"]+)"`, 'i'));
  return match ? match[1] : '';
}

function parseDirectoryPackageVersions() {
  const file = path.join(repoRoot, 'Directory.Packages.props');
  const versions = new Map();
  if (!fs.existsSync(file)) return versions;

  const text = readText(file);
  const properties = parseMsBuildProperties(text);
  for (const match of text.matchAll(/<PackageVersion\b[^>]*>/gi)) {
    const tag = match[0];
    const name = xmlAttr(tag, 'Include');
    const version = resolveMsBuildProperties(xmlAttr(tag, 'Version'), properties);
    if (name) versions.set(name.toLowerCase(), version);
  }
  return versions;
}

function parseMsBuildProperties(text) {
  const properties = new Map();
  for (const match of text.matchAll(/<([A-Za-z_][A-Za-z0-9_.-]*)>([^<]+)<\/\1>/g)) {
    const name = match[1];
    if (name === 'PackageVersion' || name === 'PropertyGroup' || name === 'ItemGroup') continue;
    properties.set(name, match[2].trim());
  }
  return properties;
}

function resolveMsBuildProperties(value, properties) {
  if (!value) return value;
  return value.replace(/\$\(([^)]+)\)/g, (_, name) => properties.get(name) || `$(${name})`);
}

function classifyProject(filePath) {
  const rel = path.relative(repoRoot, filePath).replace(/\\/g, '/');
  if (rel.startsWith('tests/')) return 'test';
  if (rel.includes('.Benchmarks')) return 'development';
  return 'runtime';
}

function parsePackageReferences() {
  const centralVersions = parseDirectoryPackageVersions();
  const projects = walk(repoRoot, file => file.endsWith('.csproj'));
  const packages = new Map();

  for (const project of projects) {
    const text = readText(project);
    const relProject = path.relative(repoRoot, project).replace(/\\/g, '/');
    for (const match of text.matchAll(/<PackageReference\b[^>]*>/gi)) {
      const tag = match[0];
      const name = xmlAttr(tag, 'Include') || xmlAttr(tag, 'Update');
      if (!name) continue;

      const key = name.toLowerCase();
      const existing = packages.get(key) || {
        name,
        version: '',
        license: '',
        licenseType: '',
        projectUrl: '',
        licenseUrl: '',
        usage: new Set(),
        projects: []
      };

      existing.version = xmlAttr(tag, 'Version') || centralVersions.get(key) || existing.version;
      existing.usage.add(classifyProject(project));
      existing.projects.push(relProject);
      packages.set(key, existing);
    }
  }

  for (const pkg of packages.values()) {
    Object.assign(pkg, readNuGetMetadata(pkg.name, pkg.version));
  }

  return [...packages.values()].sort((a, b) => a.name.localeCompare(b.name));
}

function readNuGetMetadata(packageName, desiredVersion) {
  const packageDir = path.join(os.homedir(), '.nuget', 'packages', packageName.toLowerCase());
  if (!fs.existsSync(packageDir)) return {};

  let versionDir = '';
  if (desiredVersion && !desiredVersion.includes('$(')) {
    const candidate = path.join(packageDir, desiredVersion.toLowerCase());
    if (fs.existsSync(candidate)) versionDir = candidate;
  }

  if (!versionDir) {
    const dirs = fs.readdirSync(packageDir, { withFileTypes: true })
      .filter(entry => entry.isDirectory())
      .map(entry => path.join(packageDir, entry.name))
      .sort();
    versionDir = dirs[dirs.length - 1] || '';
  }

  if (!versionDir) return {};
  const nuspec = fs.readdirSync(versionDir).find(name => name.endsWith('.nuspec'));
  if (!nuspec) return {};

  const text = readText(path.join(versionDir, nuspec));
  return {
    version: extractXmlText(text, 'version') || desiredVersion,
    license: extractLicense(text),
    licenseType: extractXmlAttrFromElement(text, 'license', 'type'),
    projectUrl: extractXmlText(text, 'projectUrl'),
    licenseUrl: extractXmlText(text, 'licenseUrl')
  };
}

function extractXmlText(text, element) {
  const match = text.match(new RegExp(`<${element}[^>]*>([\\s\\S]*?)<\\/${element}>`, 'i'));
  return match ? decodeXml(match[1].trim()) : '';
}

function extractXmlAttrFromElement(text, element, attr) {
  const match = text.match(new RegExp(`<${element}\\b([^>]*)>`, 'i'));
  return match ? xmlAttr(match[1], attr) : '';
}

function extractLicense(text) {
  const license = extractXmlText(text, 'license');
  if (license) return license;
  return extractXmlText(text, 'licenseUrl');
}

function decodeXml(value) {
  // Decode &amp; LAST so an already-escaped entity such as "&amp;lt;" decodes to the
  // literal "&lt;" rather than being double-decoded into "<".
  return value
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&quot;/g, '"')
    .replace(/&apos;/g, "'")
    .replace(/&amp;/g, '&');
}

function parseNpmPackages() {
  const packageJsonFiles = [
    'src/etl-sql-vscode/package.json',
    'src/etl-sql-vscode/ui/package.json'
  ].map(file => path.join(repoRoot, file)).filter(fs.existsSync);

  const packages = new Map();
  for (const file of packageJsonFiles) {
    const rel = path.relative(repoRoot, file).replace(/\\/g, '/');
    const json = JSON.parse(readText(file));
    addNpmDeps(packages, json.dependencies || {}, 'runtime', rel);
    addNpmDeps(packages, json.devDependencies || {}, 'development', rel);
  }

  for (const pkg of packages.values()) {
    const metadata = readNpmPackageMetadata(pkg.name);
    pkg.license = metadata.license || npmLicenseFallbacks[pkg.name] || '';
    pkg.projectUrl = metadata.homepage || metadata.repository || '';
  }

  return [...packages.values()].sort((a, b) => a.name.localeCompare(b.name));
}

function addNpmDeps(packages, deps, usage, source) {
  for (const [name, version] of Object.entries(deps)) {
    const existing = packages.get(name) || {
      name,
      version,
      license: '',
      projectUrl: '',
      usage: new Set(),
      sources: []
    };
    existing.usage.add(usage);
    existing.sources.push(source);
    packages.set(name, existing);
  }
}

function readNpmPackageMetadata(packageName) {
  const candidates = [
    path.join(repoRoot, 'src', 'etl-sql-vscode', 'node_modules', packageName, 'package.json'),
    path.join(repoRoot, 'src', 'etl-sql-vscode', 'ui', 'node_modules', packageName, 'package.json')
  ];

  for (const candidate of candidates) {
    if (!fs.existsSync(candidate)) continue;
    const json = JSON.parse(readText(candidate));
    return {
      license: typeof json.license === 'string' ? json.license : '',
      homepage: json.homepage || '',
      repository: normalizeRepository(json.repository)
    };
  }

  return {};
}

function normalizeRepository(repository) {
  if (!repository) return '';
  if (typeof repository === 'string') return repository;
  return repository.url || '';
}

function usageText(set) {
  const order = ['runtime', 'development', 'test'];
  return order.filter(value => set.has(value)).join(', ');
}

function md(value) {
  const text = value == null || value === '' ? 'TBD' : String(value);
  // Escape the backslash escape character together with the pipe so a value containing
  // a literal backslash cannot defeat the table-cell escaping.
  return text.replace(/([\\|])/g, '\\$1').replace(/\r?\n/g, ' ');
}

function render() {
  const nuget = parsePackageReferences();
  const npm = parseNpmPackages();
  return `# Third-Party Dependency Inventory

Generated by \`node scripts/generate-third-party-inventory.js\`.

This file is an inventory aid for release review. Use it to update
\`THIRD-PARTY-NOTICES.md\`, installer notices, container notices, and product
About screens. It is not legal advice.

## Bundled Browser Assets

| Component | Files | License | Project |
| :--- | :--- | :--- | :--- |
${bundledAssets.map(asset => `| ${md(asset.component)} | ${md(asset.files)} | ${md(asset.license)} | ${md(asset.project)} |`).join('\n')}

## Direct NuGet Packages

| Package | Version | Usage | License | License type | Project URL | Referenced by |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
${nuget.map(pkg => `| ${md(pkg.name)} | ${md(pkg.version)} | ${md(usageText(pkg.usage))} | ${md(pkg.license)} | ${md(pkg.licenseType)} | ${md(pkg.projectUrl || pkg.licenseUrl)} | ${md(pkg.projects.join('; '))} |`).join('\n')}

## Direct npm Packages

| Package | Version range | Usage | License | Project URL | Source manifests |
| :--- | :--- | :--- | :--- | :--- | :--- |
${npm.map(pkg => `| ${md(pkg.name)} | ${md(pkg.version)} | ${md(usageText(pkg.usage))} | ${md(pkg.license)} | ${md(pkg.projectUrl)} | ${md(pkg.sources.join('; '))} |`).join('\n')}

## Review Notes

- Treat packages with \`TBD\`, \`package license file\`, or license-file names as requiring manual review.
- Confirm whether test/development-only dependencies are redistributed in the artifact being shipped.
- Preserve license banners in bundled JavaScript and CSS files.
- Re-run this script after dependency upgrades and before publishing release artifacts.
`;
}

const output = render();

if (checkOnly) {
  if (!fs.existsSync(outputPath)) {
    console.error(`${path.relative(repoRoot, outputPath)} does not exist. Run the generator first.`);
    process.exit(1);
  }
  const current = readText(outputPath);
  if (current !== output) {
    console.error(`${path.relative(repoRoot, outputPath)} is out of date. Run: node scripts/generate-third-party-inventory.js`);
    process.exit(1);
  }
  console.log(`${path.relative(repoRoot, outputPath)} is up to date.`);
} else {
  writeText(outputPath, output);
  console.log(`Wrote ${path.relative(repoRoot, outputPath)}`);
}
