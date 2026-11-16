/**
 * The documents the language client hands to the server. The server registers every feature for
 * both `etlsql` and `rptsql`, and the notebook path sends `vscode-notebook-cell` context, so each
 * of those has to be selected here or the server never sees it.
 */
export const ETL_DOCUMENT_SELECTOR = [
    { scheme: 'file', language: 'etlsql' },
    { scheme: 'untitled', language: 'etlsql' },
    { scheme: 'file', language: 'rptsql' },
    { scheme: 'untitled', language: 'rptsql' },
    { scheme: 'vscode-notebook-cell', language: 'etlsql' }
];

/** Files whose changes on disk the server is told about. */
export const ETL_FILE_WATCH_GLOB = '**/*.{etlsql,rptsql}';
