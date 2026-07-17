const fs = require('fs');
const path = require('path');

const repoRoot = path.resolve(__dirname, '..');

// Map old folder keys to new folder names
const folderMap = {
  'Docs/Reference/Grammar.md': 'docs/guides/getting-started.md',
  'Docs/Reference/Data_Connectors.md': 'docs/guides/administration.md',
  'Docs/Reference/Standard_Library.md': 'docs/guides/getting-started.md',
  'Docs/Reference/Specialized_Operations.md': 'docs/guides/administration.md',
  'Docs/Reference/RelativeDate_Parameters.md': 'docs/reference/dates-times/reldate.md',
  'Docs/RelativeDate_Parameters.md': 'docs/reference/dates-times/reldate.md',
  'Docs/Report_SQL_Guide.md': 'docs/guides/report-sql.md',
  'Docs/Administrators_Guide.md': 'docs/guides/administration.md',
  'Docs/Cookbook.md': 'docs/cookbooks/etl-recipes.md',
  'Docs/Report_Cookbook.md': 'docs/cookbooks/report-recipes.md',
  'Docs/Reference/Lineage.md': 'docs/reference/statements/lineage.md',
  'Docs/Sample_Guide.md': 'docs/guides/sample-guide.md',
  'Docs/FAQ.md': 'docs/guides/faq.md',
  'Docs/Migration_Guide.md': 'docs/guides/migration-guide.md',
  'Docs/Testing.md': 'docs/guides/testing.md',
  'Docs/Pipelines_and_DAGs.md': 'docs/guides/pipelines-and-dags.md',
  'Docs/ETL_Notebook_Guide.md': 'docs/guides/notebook-guide.md',
  'Docs/Release_Checklist.md': 'docs/guides/release-checklist.md',
  'Docs/ReportPortal_Administrators_Guide.md': 'docs/guides/portal-admin.md',
  'Docs/ReportPortal_User_Guide.md': 'docs/guides/portal-user.md',
  'Docs/Design/': 'docs/architecture/decisions/',
  'Docs/Strategy/': 'docs/architecture/roadmaps/',
  'Docs/Standards/': 'docs/architecture/standards/',
  'Docs/Operations/': 'docs/architecture/decisions/',
};

// Help sub-paths
const helpMapping = [
  { match: /src\/ETL-SQL\.Core\/Resources\/Help\/Connectors\/([a-zA-Z0-9_]+)\.md/i, replace: (name) => {
    const n = name.toLowerCase();
    if (['mssql', 'postgres', 'oracle', 'sqlite', 'mysql', 'odbc', 'snowflake', 'bigquery', 'mongodb', 'neo4j'].includes(n)) {
      return `docs/reference/connectors/databases/${n}.md`;
    }
    if (['flatfile', 'excel', 'json', 'xml', 'parquet', 'avro'].includes(n)) {
      return `docs/reference/connectors/files/${n}.md`;
    }
    return `docs/reference/connectors/services/${n}.md`;
  }},
  { match: /src\/ETL-SQL\.Core\/Resources\/Help\/Functions\/([a-zA-Z0-9_]+)\.md/i, replace: (name) => {
    const n = name.toUpperCase();
    const categories = {
      string: ["CONCAT", "SUBSTRING", "TRIM", "REPLACE", "CHARINDEX", "UPPER", "LOWER", "LEN", "LENGTH", "ASCII", "UNICODE", "CHAR", "LEFT", "RIGHT", "LTRIM", "RTRIM", "REVERSE", "LPAD", "RPAD", "INSTR", "CONCAT_WS", "SPLIT_PART", "SPACE", "TO_STR", "PATINDEX", "REPLICATE", "REPEAT", "QUOTENAME", "DATALENGTH", "TRANSLATE", "STRING_ESCAPE", "STRING_SPLIT", "CHAR_LENGTH", "OVERLAY", "POSITION", "SUBSTR", "STUFF", "STR"],
      math: ["ABS", "ACOS", "ASIN", "ATAN", "ATAN2", "CEIL", "CEILING", "COS", "COT", "DEGREES", "EXP", "FLOOR", "LOG", "LOG10", "PI", "POWER", "RADIANS", "RAND", "ROUND", "SIGN", "SIN", "SQRT", "SQUARE", "TAN"],
      datetime: ["DATEADD", "DATEDIFF", "DATEPART", "DATENAME", "GETDATE", "SYSDATE", "NOW", "YEAR", "MONTH", "DAY", "HOUR", "MINUTE", "SECOND", "EXTRACT", "CURRENT_TIMESTAMP", "CURRENT_DATE", "CURRENT_TIME", "DATE_TRUNC"],
      aggregate: ["AVG", "COUNT", "MIN", "MAX", "SUM", "STRING_AGG", "VAR", "VARP", "STDEV", "STDEVP"],
      window: ["ROW_NUMBER", "RANK", "DENSE_RANK", "NTILE", "LAG", "LEAD", "FIRST_VALUE", "LAST_VALUE", "CUME_DIST", "PERCENT_RANK"],
      cryptography: ["HASHBYTES", "BINARY_CHECKSUM", "CHECKSUM", "ENCRYPTBYKEY", "DECRYPTBYKEY"],
      conversion: ["CAST", "TRY_CAST", "COALESCE", "ISNULL", "NULLIF", "CONVERT", "IIF"],
      "json-xml": ["JSON_VALUE", "JSON_QUERY", "JSON_MODIFY", "XML_VALUE", "XML_QUERY"],
      "table-valued": ["USER_GROUPS", "USER_ROLES", "SESSION_PROPERTY", "CONNECTION_PROPERTY"],
      collections: ["ADD_TO_LIST", "APPEND_TO_LIST", "LIST_COUNT", "LIST_GET", "LIST_REMOVE", "LIST_CONTAINS"]
    };
    for (const cat of Object.keys(categories)) {
      if (categories[cat].includes(n)) {
        return `docs/reference/functions/${cat}/${n}.md`;
      }
    }
    return `docs/reference/functions/general/${n}.md`;
  }},
  { match: /src\/ETL-SQL\.Core\/Resources\/Help\/Keywords\/([a-zA-Z0-9_\.]+)\.md/i, replace: (name) => {
    const n = name.toLowerCase();
    const flow = ['if', 'while', 'for', 'foreach', 'try', 'break', 'continue', 'return', 'throw', 'waitfor', 'parallel', 'run', 'go', 'execute'];
    if (flow.includes(n) || n === 'try-catch') return `docs/reference/control-flow/${n}.md`;
    if (['declare', 'set', 'use'].includes(n)) return `docs/reference/variables-parameters/${n}.md`;
    if (n === 'reldate') return `docs/reference/dates-times/reldate.md`;
    if (n === 'bulk.insert') return `docs/reference/file-operations/bulk-insert.md`;
    if (n === 'copy') return `docs/reference/file-operations/copy-file.md`;
    if (n === 'compress') return `docs/reference/file-operations/compress-file.md`;
    if (n === 'encrypt') return `docs/reference/file-operations/encrypt-file.md`;
    if (n.startsWith('portal_') || ['favorite', 'revoke'].includes(n)) return `docs/reference/portal-admin/${n.replace(/_/g, '-')}.md`;
    if (['schedule', 'kill', 'publish', 'validate', 'export', 'subscription'].includes(n)) return `docs/reference/orchestrator-jobs/${n}.md`;
    return `docs/reference/statements/${n}.md`;
  }},
  { match: /src\/ETL-SQL\.Core\/Resources\/Help\/Operations\/([a-zA-Z0-9_\.\/]+)\.md/i, replace: (name) => {
    const n = name.toLowerCase().replace(/\//g, '-');
    if (n === 'lineage') return `docs/reference/statements/lineage.md`;
    if (['file', 'directory', 'transfer', 'create-pgp-key-pair', 'create-ssh-key-pair', 'send-email', 'send-file', 'receive-file'].includes(n)) {
      return `docs/reference/file-operations/${n}.md`;
    }
    if (n === 'create.pgp_key_pair') return `docs/reference/file-operations/create-pgp-key-pair.md`;
    if (n === 'create.ssh_key_pair') return `docs/reference/file-operations/create-ssh-key-pair.md`;
    if (n === 'send-email' || n === 'send/email') return `docs/reference/file-operations/send-email.md`;
    if (n === 'send-file' || n === 'send/file') return `docs/reference/file-operations/send-file.md`;
    if (n === 'receive-file' || n === 'receive/file') return `docs/reference/file-operations/receive-file.md`;
    return `docs/reference/file-operations/${n}.md`;
  }},
  { match: /src\/ETL-SQL\.Core\/Resources\/Help\/Visuals\/([a-zA-Z0-9_]+)\.md/i, replace: (name) => {
    return `docs/reference/visuals-reporting/visuals/${name.toLowerCase()}.md`;
  }},
  { match: /src\/ETL-SQL\.Core\/Resources\/Help\/Report\/([a-zA-Z0-9_]+)\.md/i, replace: (name) => {
    return `docs/reference/visuals-reporting/report/${name.toLowerCase()}.md`;
  }},
  { match: /src\/ETL-SQL\.Core\/Resources\/Help\/Options\/([a-zA-Z0-9_]+)\.md/i, replace: (name) => {
    return `docs/reference/configuration/${name.toLowerCase()}.md`;
  }},
  { match: /src\/ETL-SQL\.Core\/Resources\/Help\/Variables\/([a-zA-Z0-9_@]+)\.md/i, replace: (name) => {
    return `docs/reference/variables-parameters/${name.toLowerCase()}.md`;
  }},
];

// Helper to find all markdown files recursively (excluding Docs_Legacy and node_modules)
function getMarkdownFiles(dir) {
  let results = [];
  const list = fs.readdirSync(dir);
  list.forEach(file => {
    const fullPath = path.join(dir, file);
    const stat = fs.statSync(fullPath);
    if (stat && stat.isDirectory()) {
      if (file !== 'Docs_Legacy' && file !== 'node_modules' && file !== '.git' && file !== 'bin' && file !== 'obj') {
        results = results.concat(getMarkdownFiles(fullPath));
      }
    } else if (file.endsWith('.md')) {
      results.push(fullPath);
    }
  });
  return results;
}

const mdFiles = getMarkdownFiles(repoRoot);

console.log(`Scanning ${mdFiles.length} markdown files for link migration...`);

let migratedCount = 0;

mdFiles.forEach(filePath => {
  let content = fs.readFileSync(filePath, 'utf8');
  let originalContent = content;

  // 1. Map old markdown links [label](path)
  content = content.replace(/\[([^\]]+)\]\(([^)]+)\)/g, (match, label, link) => {
    // Web links are ignored
    if (link.startsWith('http') || link.startsWith('#')) return match;

    const hashIndex = link.indexOf('#');
    let cleanLink = hashIndex !== -1 ? link.substring(0, hashIndex) : link;
    const anchor = hashIndex !== -1 ? link.substring(hashIndex) : '';

    // Convert relative syntax in path
    let lookupLink = cleanLink.replace(/\\/g, '/');
    
    // Resolve relative path to absolute repository root prefix
    const fileDir = path.dirname(filePath);
    const resolvedAbs = path.resolve(fileDir, lookupLink);
    const relativeToRoot = path.relative(repoRoot, resolvedAbs).replace(/\\/g, '/');

    // Check if it matches any folderMap entry
    let mapped = null;
    for (const key of Object.keys(folderMap)) {
      if (relativeToRoot.startsWith(key)) {
        mapped = relativeToRoot.replace(key, folderMap[key]);
        break;
      }
      if (relativeToRoot === key || relativeToRoot + '.md' === key) {
        mapped = folderMap[key];
        break;
      }
    }

    // Check help mappings
    if (!mapped) {
      for (const mapping of helpMapping) {
        const matchResult = relativeToRoot.match(mapping.match);
        if (matchResult) {
          mapped = mapping.replace(matchResult[1]);
          break;
        }
      }
    }

    if (mapped) {
      // Re-resolve to relative path from file path
      const targetAbs = path.resolve(repoRoot, mapped);
      let newRelative = path.relative(fileDir, targetAbs).replace(/\\/g, '/');
      return `[${label}](${newRelative}${anchor})`;
    }

    return match;
  });

  // 2. Also map plain string mentions of paths (e.g. "Docs/Reference/Grammar.md")
  for (const key of Object.keys(folderMap)) {
    const searchVal = key;
    const replaceVal = folderMap[key];
    content = content.replaceAll(searchVal, replaceVal);
    // Also map lowercase versions
    content = content.replaceAll(searchVal.toLowerCase(), replaceVal.toLowerCase());
  }

  if (content !== originalContent) {
    fs.writeFileSync(filePath, content, 'utf8');
    migratedCount++;
  }
});

console.log(`Link migration complete. Updated ${migratedCount} files.`);
