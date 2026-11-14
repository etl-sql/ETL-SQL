/**
 * Pure policy evaluation and sealing helpers for ETL-SQL VS Code scripts.
 * Kept independent of the vscode host module so save/run policy rules have direct unit coverage.
 */

export type SavePolicyDecision =
    | { action: 'none' }
    | { action: 'warn-plaintext' }
    | { action: 'auto-seal'; needsPassword: boolean; literalUsePassword?: string };

/**
 * Strips `--` line comments and block comments outside string literals so commented-out
 * directives or credentials do not trigger save/run policy actions.
 */
export function stripSqlComments(text: string): string {
    let out = '';
    let i = 0;
    const len = text.length;
    while (i < len) {
        const ch = text[i];
        const next = i + 1 < len ? text[i + 1] : '';

        if (ch === '\'' || ch === '"') {
            const quote = ch;
            out += ch;
            i++;
            while (i < len) {
                const cur = text[i];
                out += cur;
                i++;
                if (cur === quote) {
                    if (quote === '\'' && i < len && text[i] === '\'') {
                        out += text[i];
                        i++;
                        continue;
                    }
                    break;
                }
            }
            continue;
        }

        if (ch === '-' && next === '-') {
            while (i < len && text[i] !== '\n') {
                i++;
            }
            continue;
        }

        if (ch === '/' && next === '*') {
            i += 2;
            while (i < len && !(text[i] === '*' && i + 1 < len && text[i + 1] === '/')) {
                i++;
            }
            i += 2;
            out += ' ';
            continue;
        }

        out += ch;
        i++;
    }
    return out;
}

export function lastOnOffSetting(text: string, settingName: string): boolean {
    const cleaned = stripSqlComments(text);
    const pattern = new RegExp(`\\bSET\\s+${settingName}\\s*(?:=\\s*)?(ON|OFF)\\b`, 'gi');
    let enabled = false;
    let match: RegExpExecArray | null;
    while ((match = pattern.exec(cleaned)) !== null) {
        enabled = match[1].toUpperCase() === 'ON';
    }
    return enabled;
}

export function extractLiteralUsePassword(text: string): string | undefined {
    const cleaned = stripSqlComments(text);
    const single = cleaned.match(/\bUSE\s+PASSWORD\s*=\s*'((?:''|[^'])*)'\s*;?/i);
    if (single) {
        return single[1].replace(/''/g, '\'');
    }
    const dbl = cleaned.match(/\bUSE\s+PASSWORD\s*=\s*"([^"]*)"\s*;?/i);
    return dbl ? dbl[1] : undefined;
}

export function hasUsePasswordPrompt(text: string): boolean {
    const cleaned = stripSqlComments(text);
    return /\bUSE\s+PASSWORD\s+PROMPT\b/i.test(cleaned);
}

export interface SealPasswordRequirement {
    needsPassword: boolean;
    literalUsePassword?: string;
}

/**
 * Computes whether sealing `text` needs a password and extracts any literal `USE PASSWORD`.
 */
function inspectScriptSaveState(text: string) {
    const cleaned = stripSqlComments(text);
    const allowPlaintextSecrets = lastOnOffSetting(cleaned, 'ALLOW_PLAINTEXT_SECRETS');
    const noSaveSensitive = lastOnOffSetting(cleaned, 'NO_SAVE_SENSITIVE');
    const noSaveConnection = lastOnOffSetting(cleaned, 'NO_SAVE_CONNECTION');
    const connectionEncryption = lastOnOffSetting(cleaned, 'CONNECTION_ENCRYPTION');

    const withoutUsePassword = cleaned.replace(/\bUSE\s+PASSWORD\s*=\s*(?:'(?:''|[^'])*'|"[^"]*")\s*;?/gi, ' ');
    const hasPlainTextCreds =
        /\b(PASSWORD|API_KEY|APIKEY|SECRET_KEY|CLIENT_SECRET|PRIVATE_KEY|PASSPHRASE|SAS_TOKEN|CONNECTION_STRING|ACCESS_TOKEN)\s*=\s*'(?!ENC:|SECRET:|<secret>)[^']+'/i.test(withoutUsePassword) ||
        /\bPassword\s*=\s*(?!['\s]|ENC:|SECRET:|<secret>)[^;'\s]+/i.test(withoutUsePassword);
    const literalUsePassword = extractLiteralUsePassword(cleaned);
    const hasLiteralUsePassword = literalUsePassword !== undefined;
    const hasConnection = /\bCREATE\s+CONNECTION\b/i.test(cleaned);
    const needsPassword = !noSaveConnection && ((connectionEncryption && hasConnection) || (!noSaveSensitive && hasPlainTextCreds));
    const hasSaveWork = hasPlainTextCreds || hasLiteralUsePassword || noSaveSensitive || noSaveConnection || (connectionEncryption && hasConnection);

    return {
        allowPlaintextSecrets,
        noSaveSensitive,
        noSaveConnection,
        connectionEncryption,
        hasPlainTextCreds,
        literalUsePassword,
        hasLiteralUsePassword,
        needsPassword,
        hasSaveWork,
    };
}

/**
 * Computes whether sealing `text` needs a password and extracts any literal `USE PASSWORD`.
 */
export function getSealPasswordRequirement(text: string): SealPasswordRequirement {
    const { needsPassword, literalUsePassword } = inspectScriptSaveState(text);
    return { needsPassword, literalUsePassword };
}

export function analyzeSavePolicy(text: string): SavePolicyDecision {
    const state = inspectScriptSaveState(text);
    if (!state.hasSaveWork) {
        return { action: 'none' };
    }

    if (
        state.allowPlaintextSecrets &&
        !state.noSaveSensitive &&
        !state.noSaveConnection &&
        !state.connectionEncryption &&
        (state.hasPlainTextCreds || state.hasLiteralUsePassword)
    ) {
        return { action: 'warn-plaintext' };
    }

    return {
        action: 'auto-seal',
        needsPassword: state.needsPassword,
        literalUsePassword: state.literalUsePassword,
    };
}

/**
 * Determines whether executing a script from VS Code requires prompting for a master password.
 * Scripts with literal `USE PASSWORD = '...'` supply their password inline (first-run sealing uses
 * that password automatically), whereas `USE PASSWORD PROMPT` or `ENC:` values require an interactive prompt.
 */
export function requiresRunPasswordPrompt(scriptText: string): boolean {
    if (extractLiteralUsePassword(scriptText) !== undefined) {
        return false;
    }
    return hasUsePasswordPrompt(scriptText) || scriptText.includes('ENC:');
}
