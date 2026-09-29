/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Declares the shared (catalog) connections a script reads from.
 *
 * On the Portal a wizard offers the catalog's aliases and writes `FROM sales_db.Sales`. Preview used
 * to declare the alias behind the author's back, so the report previewed and then failed with
 * "Unknown source" for every reader, schedule, and subscription. A script only travels with what it
 * declares, so Studio writes `CREATE CONNECTION sales_db AS MSSQL('SHARED:sales_db');` beside the
 * first read. The catalog still resolves that alias under the running user's identity, so the
 * declaration grants nothing the reader could not already use.
 */

export interface SharedConnection {
    alias: string;
    connectorType: string;
}

const NAME = /^[A-Za-z_][A-Za-z0-9_]*$/;

/** The script with comments and string literals blanked, so neither can look like a read. */
function codeOnly(script: string): string {
    return script
        .replace(/'(?:[^']|'')*'/g, "''")
        .replace(/--[^\n]*/g, '')
        .replace(/\/\*[\s\S]*?\*\//g, '');
}

/** Lower-cased names of the connections the script declares itself. */
export function declaredConnections(script: string): Set<string> {
    const names = new Set<string>();
    for (const match of codeOnly(script).matchAll(/\bCREATE\s+(?:OR\s+REPLACE\s+)?CONNECTION\s+(?:IF\s+NOT\s+EXISTS\s+)?\[?([A-Za-z_][A-Za-z0-9_]*)\]?/gi))
        names.add(match[1].toLowerCase());
    return names;
}

/** Lower-cased connection names the script reads from or writes to as `name.table`. */
export function referencedConnections(script: string): Set<string> {
    const names = new Set<string>();
    for (const match of codeOnly(script).matchAll(/\b(?:FROM|JOIN|INTO|UPDATE|USING|TABLE)\s+\[?([A-Za-z_][A-Za-z0-9_]*)\]?\s*\./gi))
        names.add(match[1].toLowerCase());
    return names;
}

/**
 * The script with a declaration added for every shared connection it uses and does not declare.
 * Returns the script unchanged when nothing is missing, so callers can compare by identity.
 */
export function declareSharedConnections(script: string, shared: readonly SharedConnection[]): string {
    if (!shared.length) return script;
    const declared = declaredConnections(script);
    const referenced = referencedConnections(script);
    const missing = shared.filter(connection =>
        NAME.test(connection.alias)
        && NAME.test(connection.connectorType)
        && referenced.has(connection.alias.toLowerCase())
        && !declared.has(connection.alias.toLowerCase()));
    if (!missing.length) return script;

    const newline = script.includes('\r\n') ? '\r\n' : '\n';
    const lines = missing.map(connection =>
        `CREATE CONNECTION ${connection.alias} AS ${connection.connectorType.toUpperCase()}('SHARED:${connection.alias}');`);
    return lines.join(newline) + newline + (script.startsWith(newline) ? '' : newline) + script;
}
