const fs = require('fs');
const os = require('os');
const path = require('path');
const crypto = require('crypto');

const repoRoot = path.resolve(__dirname, '..');
// Read the version from the single source of truth so the SBOM never goes stale on release.
const version = (() => {
  const props = fs.readFileSync(path.join(repoRoot, 'Directory.Build.props'), 'utf8');
  const match = props.match(/<VersionPrefix>(\d+\.\d+\.\d+)<\/VersionPrefix>/);
  if (!match) throw new Error('Could not read <VersionPrefix> from Directory.Build.props');
  return match[1];
})();

const bundledAssets = [
  {
    component: 'Apache ECharts',
    version: '6.0.0',
    license: 'Apache-2.0',
    project: 'https://echarts.apache.org/'
  },
  {
    component: 'Chart.js',
    version: '4.4.1',
    license: 'MIT',
    project: 'https://www.chartjs.org/'
  },
  {
    component: 'Tabulator',
    version: '5.5.0',
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
  const codeMirrorAssetMetadata = {
    '@codemirror/state': { component: 'CodeMirror @codemirror/state', project: 'https://codemirror.net/' },
    '@codemirror/view': { component: 'CodeMirror @codemirror/view', project: 'https://codemirror.net/' },
    '@codemirror/commands': { component: 'CodeMirror @codemirror/commands', project: 'https://codemirror.net/' },
    '@codemirror/language': { component: 'CodeMirror @codemirror/language', project: 'https://codemirror.net/' },
    '@codemirror/search': { component: 'CodeMirror @codemirror/search', project: 'https://codemirror.net/' },
    '@codemirror/autocomplete': { component: 'CodeMirror @codemirror/autocomplete', project: 'https://codemirror.net/' },
    '@codemirror/lint': { component: 'CodeMirror @codemirror/lint', project: 'https://codemirror.net/' },
    '@lezer/highlight': { component: 'Lezer @lezer/highlight', project: 'https://lezer.codemirror.net/' },
    'esbuild': { component: 'esbuild', project: 'https://esbuild.github.io/' }
  };
  const lockPath = path.join(repoRoot, 'scripts', 'codemirror', 'package-lock.json');
  const lock = JSON.parse(fs.readFileSync(lockPath, 'utf8'));
  const deps = Object.keys(lock.packages[''].dependencies || {});
  return deps
    .filter(name => codeMirrorAssetMetadata[name])
    .map(name => {
      const pkg = lock.packages[`node_modules/${name}`];
      const meta = codeMirrorAssetMetadata[name];
      if (!pkg?.version) throw new Error(`Missing ${name} in CodeMirror package-lock.json`);
      return {
        component: meta.component,
        version: pkg.version,
        license: pkg.license || npmLicenseFallbacks[name] || '',
        project: meta.project
      };
    });
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
        || entry.name === '.worktrees') continue;
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

  const text = fs.readFileSync(file, 'utf8');
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

function parsePackageReferences() {
  const centralVersions = parseDirectoryPackageVersions();
  const projects = walk(repoRoot, file => file.endsWith('.csproj'));
  const packages = new Map();

  for (const project of projects) {
    const text = fs.readFileSync(project, 'utf8');
    for (const match of text.matchAll(/<PackageReference\b[^>]*>/gi)) {
      const tag = match[0];
      const name = xmlAttr(tag, 'Include') || xmlAttr(tag, 'Update');
      if (!name) continue;

      const key = name.toLowerCase();
      const existing = packages.get(key) || {
        name,
        version: '',
        license: '',
        projectUrl: ''
      };

      existing.version = xmlAttr(tag, 'Version') || centralVersions.get(key) || existing.version;
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

  const text = fs.readFileSync(path.join(versionDir, nuspec), 'utf8');
  return {
    version: extractXmlText(text, 'version') || desiredVersion,
    license: extractLicense(text),
    projectUrl: extractXmlText(text, 'projectUrl')
  };
}

function extractXmlText(text, element) {
  const match = text.match(new RegExp(`<${element}[^>]*>([\\s\\S]*?)<\\/${element}>`, 'i'));
  return match ? decodeXml(match[1].trim()) : '';
}

function extractLicense(text) {
  const license = extractXmlText(text, 'license');
  if (license) return license;
  return extractXmlText(text, 'licenseUrl');
}

function decodeXml(value) {
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
    const json = JSON.parse(fs.readFileSync(file, 'utf8'));
    addNpmDeps(packages, json.dependencies || {});
    addNpmDeps(packages, json.devDependencies || {});
  }

  for (const pkg of packages.values()) {
    const metadata = readNpmPackageMetadata(pkg.name);
    pkg.license = metadata.license || npmLicenseFallbacks[pkg.name] || '';
    pkg.projectUrl = metadata.homepage || metadata.repository || '';
  }

  return [...packages.values()].sort((a, b) => a.name.localeCompare(b.name));
}

function addNpmDeps(packages, deps) {
  for (const [name, version] of Object.entries(deps)) {
    const existing = packages.get(name) || {
      name,
      version: version.replace(/^[\^~]/, ''),
      license: '',
      projectUrl: ''
    };
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
    const json = JSON.parse(fs.readFileSync(candidate, 'utf8'));
    return {
      license: typeof json.license === 'string' ? json.license : '',
      homepage: json.homepage || '',
      repository: json.repository && typeof json.repository === 'object' ? json.repository.url : json.repository || ''
    };
  }

  return {};
}

function generateSbom() {
  const nuget = parsePackageReferences();
  const npm = parseNpmPackages();

  const components = [];

  // Add NuGet packages
  for (const pkg of nuget) {
    components.push({
      type: 'library',
      name: pkg.name,
      version: pkg.version,
      purl: `pkg:nuget/${pkg.name}@${pkg.version}`,
      licenses: pkg.license ? [{ license: { name: pkg.license } }] : [],
      externalReferences: pkg.projectUrl ? [{ type: 'website', url: pkg.projectUrl }] : []
    });
  }

  // Add NPM packages
  for (const pkg of npm) {
    components.push({
      type: 'library',
      name: pkg.name,
      version: pkg.version,
      purl: `pkg:npm/${pkg.name}@${pkg.version}`,
      licenses: pkg.license ? [{ license: { name: pkg.license } }] : [],
      externalReferences: pkg.projectUrl ? [{ type: 'website', url: pkg.projectUrl }] : []
    });
  }

  // Add bundled assets
  for (const asset of bundledAssets) {
    components.push({
      type: 'library',
      name: asset.component,
      version: asset.version,
      licenses: [{ license: { name: asset.license } }],
      externalReferences: [{ type: 'website', url: asset.project }]
    });
  }

  const sbom = {
    bomFormat: 'CycloneDX',
    specVersion: '1.5',
    serialNumber: `urn:uuid:${crypto.randomUUID()}`,
    version: 1,
    metadata: {
      timestamp: new Date().toISOString(),
      component: {
        type: 'application',
        name: 'ETL-SQL',
        version: version
      }
    },
    components: components
  };

  const outDir = path.join(repoRoot, 'release');
  if (!fs.existsSync(outDir)) {
    fs.mkdirSync(outDir, { recursive: true });
  }

  fs.writeFileSync(path.join(outDir, 'sbom.json'), JSON.stringify(sbom, null, 2), 'utf8');
  console.log('Wrote sbom.json to release/');
}

generateSbom();
