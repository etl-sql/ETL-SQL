/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * rptsql-language.js — split out of designer.js, TODO.md §2.
 * The rptsql CodeMirror language: keyword sets, lazy bundle load, and highlight style.
 */

export interface StringStream {
    eatSpace(): boolean;
    match(pattern: string | RegExp): boolean;
    skipToEnd(): void;
    eol(): boolean;
    next(): string | undefined;
    peek(): string | undefined;
    current(): string;
}

export interface CodeMirrorTag {
    [key: string]: unknown;
}

export interface CodeMirrorTags {
    lineComment: CodeMirrorTag;
    blockComment: CodeMirrorTag;
    string: CodeMirrorTag;
    number: CodeMirrorTag;
    keyword: CodeMirrorTag;
    typeName: CodeMirrorTag;
    operator: CodeMirrorTag;
    variableName: CodeMirrorTag;
    propertyName: CodeMirrorTag;
    meta: CodeMirrorTag;
    bool: CodeMirrorTag;
    null: CodeMirrorTag;
    comment: CodeMirrorTag;
    function(tag: CodeMirrorTag): CodeMirrorTag;
    special(tag: CodeMirrorTag): CodeMirrorTag;
    [key: string]: unknown;
}

export interface StreamLanguageSpec {
    name: string;
    token(stream: StringStream): string | null;
    tokenTable: Record<string, unknown>;
    languageData: {
        commentTokens: { line: string; block: { open: string; close: string } };
    };
}

export interface CodeMirrorBundle {
    StreamLanguage: {
        define(spec: StreamLanguageSpec): unknown;
    };
    HighlightStyle?: {
        define(rules: Array<{ tag: unknown; class: string }>): unknown;
    };
    tags: CodeMirrorTags;
    defaultHighlightStyle?: unknown;
    [key: string]: any;
}

// rptsql token classification sets — sourced from LanguageMetadata.cs
export const _KW: Set<string> = new Set([
    'SELECT','FROM','WHERE','INSERT','UPDATE','DELETE','SET','INTO','VALUES',
    'ORDER','BY','GROUP','HAVING','LIMIT','OFFSET','TOP','DISTINCT','ALL',
    'AS','ON','CASE','WHEN','THEN','ELSE','END','WITH','OUTPUT',
    'CREATE','TABLE','DATASET','VISUAL','PAGE','SECTION','COLUMN','INDEX',
    'PROCEDURE','CONNECTION','DROP','ALTER','ADD','CONSTRAINT',
    'PRIMARY','KEY','FOREIGN','REFERENCES','DEFAULT','UNIQUE',
    'IF','WHILE','FOR','FOREACH','BEGIN','RETURN','BREAK',
    'CONTINUE','TRY','CATCH','THROW','DECLARE','PRINT','EXEC','EXECUTE',
    'JOIN','INNER','LEFT','RIGHT','FULL','OUTER','CROSS','APPLY','UNION',
    'INTERSECT','EXCEPT',
    'AND','OR','NOT','LIKE','IN','IS','BETWEEN','EXISTS','ANY','SOME',
    'NULL','TRUE','FALSE',
    'REQUIRE','VERSION','RUN','SCRIPT','USE','LOAD','SAVE','EXPORT','IMPORT',
]);

export const _FUNC: Set<string> = new Set([
    'CAST','CONVERT','COUNT','SUM','AVG','MIN','MAX','FIRST','LAST',
    'ROW_NUMBER','RANK','DENSE_RANK','NTILE','LAG','LEAD',
    'FIRST_VALUE','LAST_VALUE','PERCENTILE_CONT','PERCENTILE_DISC',
    'COALESCE','NULLIF','IIF','ISNULL','NVL',
    'UPPER','LOWER','TRIM','LTRIM','RTRIM','LEN','LENGTH','SUBSTRING',
    'REPLACE','STUFF','CHARINDEX','PATINDEX','CONCAT','FORMAT',
    'YEAR','MONTH','DAY','DATEPART','DATEDIFF','DATEADD','GETDATE','NOW',
    'SYSDATETIME','CURRENT_TIMESTAMP',
    'ABS','CEILING','FLOOR','ROUND','POWER','SQRT','SIGN','RAND',
    'NEWID','CHECKSUM','HASHBYTES',
    'STRING_AGG','LISTAGG','ARRAY_AGG','JSON_VALUE','JSON_QUERY',
]);

export const _TYPE: Set<string> = new Set([
    'INT','INTEGER','TINYINT','SMALLINT','BIGINT',
    'DECIMAL','NUMERIC','FLOAT','REAL','MONEY','SMALLMONEY',
    'VARCHAR','NVARCHAR','CHAR','NCHAR','TEXT','NTEXT',
    'DATETIME','DATETIME2','DATE','TIME','DATETIMEOFFSET','TIMESTAMP',
    'BIT','BINARY','VARBINARY','IMAGE',
    'UNIQUEIDENTIFIER','XML','JSON','CURSOR','VARIANT',
]);

// Lazy-load the CodeMirror bundle once; subsequent calls reuse the same promise.
let _cmPromise: Promise<CodeMirrorBundle> | null = null;
/**
 * Lazy-loads the CodeMirror bundle once; subsequent calls reuse the same promise.
 * @returns {Promise<Object>}
 */
export function _loadCm(): Promise<CodeMirrorBundle> {
    if (!_cmPromise) _cmPromise = (import('./codemirror/codemirror-bundle.min.js') as unknown) as Promise<CodeMirrorBundle>;
    return _cmPromise;
}

// Cached rptsql StreamLanguage instance (shared across all editor instances).
let _rptsqlLang: unknown = null;
/**
 * Cached rptsql StreamLanguage instance (shared across all editor instances).
 * @param {Object} cm The CodeMirror bundle
 * @returns {Object}
 */
export function _getRptsqlLang(cm: CodeMirrorBundle): unknown {
    if (_rptsqlLang) return _rptsqlLang;
    const { StreamLanguage, tags: t } = cm;
    _rptsqlLang = StreamLanguage.define({
        name: 'rptsql',
        token(stream: StringStream) {
            if (stream.eatSpace()) return null;
            if (stream.match('--'))  { stream.skipToEnd(); return 'lineComment'; }
            if (stream.match('/*'))  {
                while (!stream.eol()) { if (stream.match('*/')) break; stream.next(); }
                return 'blockComment';
            }
            const ch = stream.peek();
            if (ch === "'" || ch === '"') {
                stream.next();
                while (!stream.eol() && stream.next() !== ch) { /* advance to the closing quote */ }
                return 'string';
            }
            if (ch === '[') {
                stream.next();
                while (!stream.eol() && stream.next() !== ']') { /* advance to the closing bracket */ }
                return 'quotedId';
            }
            if (stream.match(/^[0-9]+\.?[0-9]*/)) return 'number';
            if (stream.match(/^[a-zA-Z_@#][a-zA-Z0-9_@#$]*/)) {
                const word = stream.current().toUpperCase();
                if (_KW.has(word))   return 'keyword';
                if (_FUNC.has(word)) return 'fn';
                if (_TYPE.has(word)) return 'typeName';
                return null;
            }
            if (stream.match(/^(<>|!=|>=|<=|=>|->|::)/)) return 'op';
            if (stream.match(/^[=<>!+\-*/&|^~%]/))      return 'op';
            stream.next();
            return null;
        },
        tokenTable: {
            lineComment:  t.lineComment,
            blockComment: t.blockComment,
            string:       t.string,
            number:       t.number,
            keyword:      t.keyword,
            fn:           t.function(t.variableName),
            typeName:     t.typeName,
            quotedId:     t.special(t.variableName),
            op:           t.operator,
        },
        languageData: {
            commentTokens: { line: '--', block: { open: '/*', close: '*/' } },
        },
    });
    return _rptsqlLang;
}

// Cached rptsql highlight style (shared across all editor instances).
//
// CodeMirror's `defaultHighlightStyle` bakes light-mode colours into generated
// class names — its keyword purple (#708) is unreadable on a dark background and
// cannot be overridden from CSS. We map each tag to a stable class instead so the
// palette lives in designer.css and can follow the host's light/dark theme.
let _rptsqlHighlight: unknown = null;
/**
 * Cached rptsql highlight style (shared across all editor instances).
 * @param {Object} cm The CodeMirror bundle
 * @returns {Object}
 */
export function _getRptsqlHighlightStyle(cm: CodeMirrorBundle): unknown {
    if (_rptsqlHighlight) return _rptsqlHighlight;
    const { HighlightStyle, tags: t, defaultHighlightStyle } = cm;
    if (typeof HighlightStyle?.define !== 'function') return defaultHighlightStyle;
    _rptsqlHighlight = HighlightStyle.define([
        { tag: t.keyword,                  class: 'etlsql-tok-keyword' },
        { tag: t.typeName,                 class: 'etlsql-tok-type' },
        { tag: t.function(t.variableName), class: 'etlsql-tok-function' },
        { tag: t.string,                   class: 'etlsql-tok-string' },
        { tag: t.number,                   class: 'etlsql-tok-number' },
        { tag: t.operator,                 class: 'etlsql-tok-operator' },
        { tag: t.special(t.variableName),  class: 'etlsql-tok-quoted-id' },
        { tag: t.variableName,             class: 'etlsql-tok-variable' },
        { tag: t.propertyName,             class: 'etlsql-tok-property' },
        { tag: t.meta,                     class: 'etlsql-tok-meta' },
        { tag: [t.bool, t.null],           class: 'etlsql-tok-atom' },
        { tag: [t.comment, t.lineComment, t.blockComment], class: 'etlsql-tok-comment' },
    ]);
    return _rptsqlHighlight;
}
