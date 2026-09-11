import assert from 'node:assert/strict';
import { _KW, _FUNC, _TYPE, _getRptsqlLang, _getRptsqlHighlightStyle } from '../src/ETL-SQL.ReportRuntime/Resources/Shared/designer/rptsql-language.js';

// 1. Keyword, function, and type classification sets
assert.ok(_KW.has('SELECT'));
assert.ok(_KW.has('DATASET'));
assert.ok(_KW.has('VISUAL'));
assert.ok(_KW.has('PAGE'));
assert.ok(_KW.has('CREATE'));
assert.ok(!_KW.has('NONEXISTENT_KEYWORD'));

assert.ok(_FUNC.has('COUNT'));
assert.ok(_FUNC.has('SUM'));
assert.ok(_FUNC.has('COALESCE'));
assert.ok(_FUNC.has('STRING_AGG'));
assert.ok(!_FUNC.has('SELECT'));

assert.ok(_TYPE.has('INT'));
assert.ok(_TYPE.has('VARCHAR'));
assert.ok(_TYPE.has('DATETIME'));
assert.ok(_TYPE.has('JSON'));
assert.ok(!_TYPE.has('COUNT'));

// 2. Mock CodeMirror bundle to exercise StreamLanguage tokenizer
let definedSpec = null;
const mockCm = {
    StreamLanguage: {
        define(spec) {
            definedSpec = spec;
            return { spec };
        },
    },
    HighlightStyle: {
        define(rules) {
            return { rules };
        },
    },
    tags: {
        lineComment: 'tag-lineComment',
        blockComment: 'tag-blockComment',
        string: 'tag-string',
        number: 'tag-number',
        keyword: 'tag-keyword',
        typeName: 'tag-typeName',
        operator: 'tag-operator',
        variableName: 'tag-variableName',
        propertyName: 'tag-propertyName',
        meta: 'tag-meta',
        bool: 'tag-bool',
        null: 'tag-null',
        comment: 'tag-comment',
        function: tag => ({ type: 'function', tag }),
        special: tag => ({ type: 'special', tag }),
    },
};

const lang = _getRptsqlLang(mockCm);
assert.ok(lang);
// Second call returns cached instance
assert.equal(_getRptsqlLang(mockCm), lang);
assert.ok(definedSpec);
assert.equal(definedSpec.name, 'rptsql');
assert.equal(definedSpec.languageData.commentTokens.line, '--');

/** Helper to simulate a CodeMirror StringStream */
function createMockStream(input) {
    let pos = 0;
    return {
        eatSpace() {
            const start = pos;
            while (pos < input.length && /\s/.test(input[pos])) pos++;
            return pos > start;
        },
        match(pattern) {
            const remaining = input.slice(pos);
            if (typeof pattern === 'string') {
                if (remaining.startsWith(pattern)) {
                    pos += pattern.length;
                    return true;
                }
                return false;
            }
            const match = pattern.exec(remaining);
            if (match && match.index === 0) {
                pos += match[0].length;
                this._lastMatch = match[0];
                return true;
            }
            return false;
        },
        skipToEnd() {
            pos = input.length;
        },
        eol() {
            return pos >= input.length;
        },
        peek() {
            return input[pos];
        },
        next() {
            return input[pos++];
        },
        current() {
            return this._lastMatch || '';
        },
    };
}

// Test tokenizer output for language constructs
function tokenizeFirst(text) {
    const stream = createMockStream(text);
    return definedSpec.token(stream);
}

assert.equal(tokenizeFirst('   '), null);
assert.equal(tokenizeFirst('-- single line comment'), 'lineComment');
assert.equal(tokenizeFirst('/* block comment */'), 'blockComment');
assert.equal(tokenizeFirst("'single quoted string'"), 'string');
assert.equal(tokenizeFirst('"double quoted string"'), 'string');
assert.equal(tokenizeFirst('[Quoted Column Identifier]'), 'quotedId');
assert.equal(tokenizeFirst('12345'), 'number');
assert.equal(tokenizeFirst('123.45'), 'number');
assert.equal(tokenizeFirst('SELECT'), 'keyword');
assert.equal(tokenizeFirst('select'), 'keyword');
assert.equal(tokenizeFirst('COUNT'), 'fn');
assert.equal(tokenizeFirst('count'), 'fn');
assert.equal(tokenizeFirst('VARCHAR'), 'typeName');
assert.equal(tokenizeFirst('varchar'), 'typeName');
assert.equal(tokenizeFirst('my_table'), null);
assert.equal(tokenizeFirst('<>'), 'op');
assert.equal(tokenizeFirst('!='), 'op');
assert.equal(tokenizeFirst('::'), 'op');
assert.equal(tokenizeFirst('='), 'op');
assert.equal(tokenizeFirst('+'), 'op');

// 3. HighlightStyle caching and rules
const highlight = _getRptsqlHighlightStyle(mockCm);
assert.ok(highlight);
assert.equal(_getRptsqlHighlightStyle(mockCm), highlight);
assert.ok(Array.isArray(highlight.rules));
const classes = highlight.rules.map(r => r.class);
assert.ok(classes.includes('etlsql-tok-keyword'));
assert.ok(classes.includes('etlsql-tok-type'));
assert.ok(classes.includes('etlsql-tok-function'));
assert.ok(classes.includes('etlsql-tok-string'));
assert.ok(classes.includes('etlsql-tok-number'));
assert.ok(classes.includes('etlsql-tok-operator'));
assert.ok(classes.includes('etlsql-tok-quoted-id'));
assert.ok(classes.includes('etlsql-tok-comment'));

console.log('rptsql language: classification sets, tokenizer matching, and highlight style passed.');
