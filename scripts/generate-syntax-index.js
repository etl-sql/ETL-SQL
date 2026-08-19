#!/usr/bin/env node
const fs = require('fs');
const path = require('path');

const repoRoot = path.resolve(__dirname, '..');
const languageMetadataPath = path.join(repoRoot, 'src', 'ETL-SQL.Core', 'Common', 'LanguageMetadata.cs');
const stewardshipTagCatalogPath = path.join(repoRoot, 'src', 'ETL-SQL.Core', 'Common', 'StewardshipTagCatalog.cs');
const syntaxIndexPath = path.join(repoRoot, 'docs', 'syntax-index.md');

const beginMarker = '<!-- BEGIN GENERATED CANONICAL TOKEN INDEX -->';
const endMarker = '<!-- END GENERATED CANONICAL TOKEN INDEX -->';

function readText(filePath) {
  return fs.readFileSync(filePath, 'utf8').replace(/\r\n/g, '\n');
}

function escapeRegex(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

function extractSet(source, name) {
  const pattern = new RegExp(
    String.raw`public\s+static\s+readonly\s+HashSet<string>\s+${name}\s*=\s*new\([^)]*\)\s*\{([\s\S]*?)\n\s*\};`,
    'm'
  );
  const match = source.match(pattern);
  if (!match) {
    throw new Error(`Unable to locate ${name} in LanguageMetadata.cs`);
  }

  const values = [];
  const tokenPattern = /"((?:[^"\\]|\\.)*)"/g;
  let tokenMatch;
  while ((tokenMatch = tokenPattern.exec(match[1])) !== null) {
    values.push(tokenMatch[1]);
  }
  return values;
}

function unique(values) {
  const seen = new Set();
  const result = [];
  for (const value of values) {
    if (!seen.has(value)) {
      seen.add(value);
      result.push(value);
    }
  }
  return result;
}

function extractStewardshipTags(source) {
  const definitions = source.match(
    /private\s+static\s+readonly\s+StewardshipTagDefinition\[\]\s+Definitions\s*=\s*\[([\s\S]*?)\n\s*\];/m
  );
  if (!definitions) {
    throw new Error('Unable to locate Definitions in StewardshipTagCatalog.cs');
  }

  const tags = [];
  const definitionPattern = /new\("([^"]+)"\s*,\s*StewardshipTagValueKind\.\w+\s*,\s*(?:\w+|\[[^\]]*\])\s*,\s*\[[^\]]*\]\s*,\s*\[([^\]]*)\]/g;
  let match;
  while ((match = definitionPattern.exec(definitions[1])) !== null) {
    tags.push(match[1]);
    const aliasPattern = /"([^"]+)"/g;
    let alias;
    while ((alias = aliasPattern.exec(match[2])) !== null) tags.push(alias[1]);
  }
  if (tags.length === 0) {
    throw new Error('Unable to extract stewardship tags from StewardshipTagCatalog.cs');
  }
  return tags;
}

function renderRows(tokens, family, notePrefix) {
  return tokens.map(token => `| \`${token}\` | ${family} | ${notePrefix} |`).join('\n');
}

function renderGeneratedSection(data) {
  const CATEGORIES = {
    "Settings & Engine Configuration": [
        "WEEK_START_DAY", "SCRIPT_HASH_POLICY", "CASE_SENSITIVE", "PROFILING", "PROFILE",
        "WHAT_IF", "ALLOW_FILE_OPERATIONS", "ALLOW_RECURSIVE_LAYERS", "TELEMETRY",
        "INTERACTIVE_MODE", "LINT", "VERSION", "CONFIG", "MAX_RECURSIVE_DEPTH",
        "MAX_IN_MEMORY_BATCHES", "FOREACH_PAGE_SIZE", "MAX_MESSAGES", "MAX_FILE_OPERATIONS",
        "MAX_PARALLEL_DEGREE", "MAX_STRING_RESULT_SIZE", "REGEX_MATCH_TIMEOUT",
        "MAX_LAST_RESULT_ROWS", "MAX_GENERATE_ROWS", "MAX_SMTP_EMAILS_PER_SCRIPT",
        "MAX_INTERNAL_OPERATIONS", "SPILL_ENCRYPTION", "SPILL_COMPRESSION", "MAX_GROUPING_SETS",
        "SET_CUBE_LIMIT", "MAX_SESSION_SIZE", "JOIN_SPILL_THRESHOLD", "TEMP_TABLE_SPILL_THRESHOLD",
        "EXTERNAL_HASH_PARTITIONS", "EXTERNAL_SORT_CHUNK_SIZE", "WINDOW_SPILL_THRESHOLD"
    ],
    "File & Directory Operations": [
        "COPY", "MOVE", "DELETE", "COMPRESS", "DECOMPRESS", "RENAME", "COPY_FILE",
        "MOVE_FILE", "RENAME_FILE", "DELETE_FILE", "COMPRESS_FILE", "DECOMPRESS_FILE",
        "ENCRYPT_FILE", "DECRYPT_FILE", "CREATE_DIRECTORY", "DELETE_DIRECTORY",
        "RENAME_DIRECTORY", "MOVE_DIRECTORY", "COPY_DIRECTORY", "DELETE_DIRECTORY_CONTENTS",
        "COMPRESS_DIRECTORY", "DECOMPRESS_DIRECTORY", "ENCRYPT_DIRECTORY", "DECRYPT_DIRECTORY",
        "PATH", "ROOT", "FILES"
    ],
    "Data Formatting & File Connector Options": [
        "SINGLEQUOTE", "DOUBLEQUOTE", "SINGLEQUOTES", "DOUBLEQUOTES", "LF", "CR",
        "CRLF", "TILDE", "SEMICOLON", "COLON", "COMMA", "TAB", "PIPE", "ESCAPE_CHAR",
        "NULL_AS", "DATE_FORMAT", "STRICT_SCHEMA", "UTF16", "LATIN1", "UNICODE",
        "BACKSLASH_N", "EMPTY", "FIELDTERMINATOR", "ROWTERMINATOR", "FIRSTROW",
        "INCLUDE_NULL_VALUES", "WITHOUT_ARRAY_WRAPPER"
    ],
    "Security & Secrets": [
        "PASSWORD", "SHOW_SECRETS", "SHOW_PASSWORD", "ALLOW_PLAINTEXT_SECRETS",
        "NO_SAVE_SENSITIVE", "NO_SAVE_CONNECTION", "CONNECTION_ENCRYPTION", "PGP_KEY",
        "PASSPHRASE"
    ],
    "Reporting & Visuals": [
        "GAUGE_STYLE", "SHOW_NO_DATA_PLACEHOLDER", "INTERACTIONS", "ON_SELECT",
        "MATCHING", "HIGHLIGHT", "SHOW_PROGRESS", "PROGRESS_STYLE", "SHOW_GOAL",
        "SHOW_PERCENT_OF_GOAL", "ABBREVIATE", "CLOSE_PCT", "MET_PCT", "COLOR_MET",
        "COLOR_CLOSE", "COLOR_MISSED", "ICON_MET", "ICON_CLOSE", "ICON_MISSED",
        "ICON_SET", "DELTA_FORMAT", "DELTA_LABEL", "TREND_DIR", "PREFIX", "SUFFIX",
        "LABEL_MET", "LABEL_CLOSE", "LABEL_MISSED", "RING", "POSITIVE_UP",
        "POSITIVE_DOWN", "TRAFFIC", "ARROWS", "CHECKS", "LAYER", "AXIS_SORT",
        "VALUE_DESC", "TOOLTIP", "NAVIGATE_PAGE", "TEMPLATE_PATH", "MINMAX",
        "FONT_SIZE", "CENTER", "INSIDE", "INSIDE_TOP", "INSIDE_BOTTOM", "INSIDE_LEFT",
        "INSIDE_RIGHT", "INSIDE_TOP_LEFT", "INSIDE_TOP_RIGHT", "INSIDE_BOTTOM_LEFT",
        "INSIDE_BOTTOM_RIGHT", "HEADER", "FOOTER", "CSS", "JS", "FAVICON", "LOGO",
        "BACKGROUND", "BAR", "HBAR", "LINE", "SCATTER", "PIE", "DONUT", "TABLE",
        "CARD", "TEXT", "SLICER", "DATEPICKER", "RELDATEPICKER", "SLIDER", "SEARCH",
        "CHECKBOX", "TEXTBOX", "NUMBERBOX", "LABEL_POSITION", "DECIMALS",
        "PLACEHOLDER", "CONTENT", "GAUGE", "FUNNEL", "WATERFALL", "BOXPLOT",
        "TREEMAP", "HEATMAP", "COMBO", "MAP", "STRUCTURE", "GAP", "MAPPINGS",
        "ACTIONS", "CLEAR_FILTERS", "DASHBOARD", "PAGINATED", "PINNABLE", "TITLE",
        "SUBTITLE", "VISIBLE"
    ],
    "Date & Time": [
        "RELDATE", "SYSDATE", "CURRENT_TIMESTAMP", "CURRENT_DATE", "CURRENT_TIME",
        "YEAR", "MONTH", "DAY", "HOUR", "MINUTE", "SECOND"
    ],
    "Email Operations": [
        "SEND", "RECEIVE", "EMAIL", "SUBJECT", "BODY", "ATTACH", "CC", "BCC",
        "RECIPIENT", "DELIVER", "SMTP"
    ],
    "Script & Job Execution": [
        "STEP", "RUN_SCRIPT", "USE", "START", "STOP", "PAUSE", "KILL", "WAIT",
        "WAITFOR", "DELAY", "UNTIL", "JOB", "SCHEDULE", "EVERY", "JOBS", "CRON",
        "RUN", "SCRIPT", "TRIGGER", "ON_LOAD", "ON_RUN", "ACTIVE"
    ],
    "Portal Administration": [
        "SUBSCRIPTION", "PUBLISH", "VALIDATE", "EXPORT", "BUNDLE", "BUNDLES",
        "PUBLISHED", "VERSIONS", "DEPENDENCIES", "FAVORITE", "UNFAVORITE",
        "CATALOG", "RECENT", "PERMISSIONS", "EFFECTIVE", "USAGE", "METRICS",
        "SHARE", "LINK", "LINKS", "EMBED", "TOKEN", "TOKENS", "SAVED", "VIEW",
        "VIEWS", "ALERT", "ALERTS", "PORTAL", "REPORT", "REPORTS", "EXPIRES",
        "EXPIRES_AT", "SHOW", "HISTORY"
    ],
    "XML, JSON & Query Modifiers": [
        "AUTO", "RAW", "EXPLICIT", "ELEMENTS", "EXPLAIN", "SEMI", "ANTI",
        "WITHIN", "AT", "TIME", "ZONE", "WITH", "RECURSIVE", "HASH", "LOOP",
        "IDENTITY", "DEFAULT", "RANGE", "GROUPS", "PRECEDING", "FOLLOWING",
        "UNBOUNDED", "CURRENT", "EXCLUDE", "NO", "OTHERS", "OVER", "PARTITION",
        "TIES", "PERCENT", "FETCH", "ROWS", "GENERATE", "SEND_FILE", "RECEIVE_FILE",
        "FILE_SEND", "FILE_RECEIVE"
    ]
  };

  const categorized = {};
  for (const key in CATEGORIES) {
    categorized[key] = [];
  }
  const generalLeftovers = [];

  for (const token of data.generalKeywords) {
    let found = false;
    for (const [cat, tokens] of Object.entries(CATEGORIES)) {
      if (tokens.some(t => t.toUpperCase() === token.toUpperCase())) {
        categorized[cat].push(token);
        found = true;
        break;
      }
    }
    if (!found) {
      generalLeftovers.push(token);
    }
  }

  const families = [
    ['DML Keywords', data.dmlKeywords],
    ['DDL Keywords', data.ddlKeywords],
    ['Control Flow Keywords', data.controlFlowKeywords],
    ['Join Keywords', data.joinKeywords],
    ['Operator Keywords', data.operatorKeywords]
  ];

  for (const [cat, tokens] of Object.entries(categorized)) {
    if (tokens.length > 0) {
      families.push([`${cat} Keywords`, tokens]);
    }
  }

  if (generalLeftovers.length > 0) {
    families.push(['General Keywords', generalLeftovers]);
  }

  const parts = [
    '## 19. Canonical Token Inventory',
    '',
    '> Generated from `src/ETL-SQL.Core/Common/LanguageMetadata.cs`. Run `node ./scripts/generate-syntax-index.js` after adding, removing, or renaming language tokens.',
    ''
  ];

  let nextIndex = 1;
  for (const [label, tokens] of families) {
    const sortedTokens = [...tokens].sort((a, b) => a.localeCompare(b, 'en', { sensitivity: 'base' }));
    parts.push(`### 19.${nextIndex} ${label}`);
    parts.push('');
    parts.push('| Token | Family | Notes |');
    parts.push('| :--- | :--- | :--- |');
    parts.push(renderRows(sortedTokens, label.replace(' Keywords', ''), 'Canonical language token'));
    parts.push('');
    nextIndex++;
  }

  const sections = [
    ['Connector Types', data.connectors, 'Connector', 'Canonical connector token'],
    ['Built-in Functions', data.functions, 'Function', 'Canonical built-in function'],
    ['Data Types', data.dataTypes, 'Type', 'Canonical data type token'],
    ['Standard Tags', data.tags.map(tag => `@${tag}`), 'Tag', 'Standard governance tag']
  ];

  for (const [title, tokens, family, note] of sections) {
    const sortedTokens = [...tokens].sort((a, b) => a.localeCompare(b, 'en', { sensitivity: 'base' }));
    parts.push(`### 19.${nextIndex} ${title}`);
    parts.push('');
    parts.push('| Token | Group | Notes |');
    parts.push('| :--- | :--- | :--- |');
    parts.push(renderRows(sortedTokens, family, note));
    parts.push('');
    nextIndex++;
  }

  return parts.join('\n').trimEnd() + '\n';
}

function upsertGeneratedSection(text, section) {
  const generatedBlock = `${beginMarker}\n${section}${endMarker}\n`;
  const markerRegex = new RegExp(`${escapeRegex(beginMarker)}[\\s\\S]*?${escapeRegex(endMarker)}\\n?`, 'm');

  if (markerRegex.test(text)) {
    return text.replace(markerRegex, generatedBlock).trimEnd() + '\n';
  }

  return `${text.trimEnd()}\n\n${generatedBlock}`;
}

function main() {
  const checkOnly = process.argv.includes('--check');
  const source = readText(languageMetadataPath);
  const stewardshipTags = readText(stewardshipTagCatalogPath);

  const data = {
    dmlKeywords: extractSet(source, 'DmlKeywords'),
    ddlKeywords: extractSet(source, 'DdlKeywords'),
    controlFlowKeywords: extractSet(source, 'ControlFlowKeywords'),
    joinKeywords: extractSet(source, 'JoinKeywords'),
    operatorKeywords: extractSet(source, 'OperatorKeywords'),
    generalKeywords: extractSet(source, 'Keywords'),
    connectors: unique(extractSet(source, 'ConnectorTypes')),
    functions: unique(extractSet(source, 'Functions')),
    dataTypes: unique(extractSet(source, 'DataTypes')),
    tags: unique(extractStewardshipTags(stewardshipTags))
  };

  const generatedSection = renderGeneratedSection(data);
  const current = readText(syntaxIndexPath);
  const updated = upsertGeneratedSection(current, generatedSection);

  if (checkOnly) {
    if (current !== updated) {
      console.error('docs/syntax-index.md is out of sync with LanguageMetadata.cs');
      process.exitCode = 1;
    }
    return;
  }

  if (current !== updated || fs.readFileSync(syntaxIndexPath, 'utf8') !== updated) {
    fs.writeFileSync(syntaxIndexPath, updated, 'utf8');
  }
}

main();
