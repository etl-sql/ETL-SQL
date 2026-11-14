import { describe, it, expect } from 'vitest';
import {
    analyzeSavePolicy,
    extractLiteralUsePassword,
    getSealPasswordRequirement,
    hasUsePasswordPrompt,
    requiresRunPasswordPrompt,
    stripSqlComments
} from '../savePolicy';

describe('savePolicy', () => {
    it('ignores commented-out directives and secrets', () => {
        const sql = [
            '-- USE PASSWORD = \'comment-pass\';',
            '/* SET ALLOW_PLAINTEXT_SECRETS = ON;',
            'CREATE CONNECTION c AS MSSQL(PASSWORD=\'secret\'); */',
            'SELECT 1;'
        ].join('\n');

        expect(stripSqlComments(sql)).not.toContain('comment-pass');
        expect(extractLiteralUsePassword(sql)).toBeUndefined();
        expect(analyzeSavePolicy(sql)).toEqual({ action: 'none' });
    });

    it('auto-seals plaintext connection passwords when ALLOW_PLAINTEXT_SECRETS is OFF', () => {
        const sql = `CREATE CONNECTION pg AS POSTGRES('Host=db;Password=my-secret');\nSELECT 1;`;

        expect(analyzeSavePolicy(sql)).toEqual({
            action: 'auto-seal',
            needsPassword: true,
            literalUsePassword: undefined
        });
    });

    it('auto-seals literal USE PASSWORD without prompting for another password', () => {
        const sql = `USE PASSWORD = 'my-master';\nCREATE CONNECTION pg AS POSTGRES('Host=db;Password=db-pass');`;

        expect(extractLiteralUsePassword(sql)).toBe('my-master');
        expect(analyzeSavePolicy(sql)).toEqual({
            action: 'auto-seal',
            needsPassword: true,
            literalUsePassword: 'my-master'
        });
        expect(getSealPasswordRequirement(sql)).toEqual({
            needsPassword: true,
            literalUsePassword: 'my-master'
        });
    });

    it('auto-seals standalone literal USE PASSWORD without needing encryption password', () => {
        const sql = `USE PASSWORD = 'only-master';\nSELECT 1;`;

        expect(analyzeSavePolicy(sql)).toEqual({
            action: 'auto-seal',
            needsPassword: false,
            literalUsePassword: 'only-master'
        });
    });

    it('unescapes doubled SQL quotes in literal USE PASSWORD', () => {
        const sql = `USE PASSWORD = 'p''w''!';`;
        expect(extractLiteralUsePassword(sql)).toBe(`p'w'!`);
    });

    it('warns instead of auto-sealing when ALLOW_PLAINTEXT_SECRETS is ON and no stricter flags apply', () => {
        const sql = `SET ALLOW_PLAINTEXT_SECRETS = ON;\nUSE PASSWORD = 'dev';`;
        expect(analyzeSavePolicy(sql)).toEqual({ action: 'warn-plaintext' });
    });

    it('auto-seals when ALLOW_PLAINTEXT_SECRETS was toggled back OFF', () => {
        const sql = [
            'SET ALLOW_PLAINTEXT_SECRETS = ON;',
            'SET ALLOW_PLAINTEXT_SECRETS = OFF;',
            `USE PASSWORD = 'dev';`
        ].join('\n');

        expect(analyzeSavePolicy(sql)).toEqual({
            action: 'auto-seal',
            needsPassword: false,
            literalUsePassword: 'dev'
        });
    });

    it('prompts on run for USE PASSWORD PROMPT or ENC:, and skips prompt for literal USE PASSWORD', () => {
        expect(hasUsePasswordPrompt('USE PASSWORD PROMPT;\nSELECT 1;')).toBe(true);
        expect(requiresRunPasswordPrompt('USE PASSWORD PROMPT;\nSELECT 1;')).toBe(true);
        expect(requiresRunPasswordPrompt(`CREATE CONNECTION c AS MSSQL(PASSWORD='ENC:abc');`)).toBe(true);
        expect(requiresRunPasswordPrompt(`USE PASSWORD = 'inline';\nCREATE CONNECTION c AS MSSQL(PASSWORD='ENC:abc');`)).toBe(false);
        expect(requiresRunPasswordPrompt('SELECT 1;')).toBe(false);
    });
});
